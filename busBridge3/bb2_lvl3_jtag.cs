// === SCOPE ===
// implements basic JTAG functionality on top of FTDI chip with lvl1/2 helper functions
using System;
using FTD2XX_busbridge3_NET;
namespace busbridge3 {
    public class ftdi_jtag {
        private byte[] tmpBuf256 = new byte[256];
        public ftdi_jtag(ftdi2_io io, uint clkDiv) {
            if(clkDiv > 0xFFFF)
                throw new Exception("invalid clkDiv (0 <= clkDiv <= 0xFFFF)");
            this.io = io;
            this.io.chk(this.io.dev.SetBitMode(0x00, FTDI.FT_BIT_MODES.FT_BIT_MODE_RESET));
            this.io.chk(this.io.dev.SetBitMode(0x00, FTDI.FT_BIT_MODES.FT_BIT_MODE_MPSSE));

            // note: The FTDI example http://www.ftdichip.com/Support/Documents/AppNotes/AN_135_MPSSE_Basics.pdf
            // sends those blocks separately (don't combine, otherwise there may be irregular startup failures,
            // e.g. JTAG ID = 0xFFFFFFF)
            byte[] buf = new byte[]{
                0x85 // disable loopback
            };
            for(int ix = 0; ix < 2; ++ix) {
                this.io.wr(buf, buf.Length);
                this.io.exec();
            }

            buf = new byte[]{
                0x8A, // disable clk divide by 5
                0x97, // disable adaptive clocking
                0x8D, //, // disable 3-phase clocking
                0xAB // invalid command, see http://www.ftdichip.com/Support/Documents/AppNotes/AN_135_MPSSE_Basics.pdf page 17
            };
            this.io.wr(buf, buf.Length, nRead :2);
            this.checkStartupReadback(this.io.exec());

            buf = new byte[]{
                0x86, // set clock divisor
                    (byte)(clkDiv & 0xFF), // value low
                    (byte)(clkDiv >> 8), // value high (0x20 for logic analyzer)        
                    0xAB // invalid command, see http://www.ftdichip.com/Support/Documents/AppNotes/AN_135_MPSSE_Basics.pdf page 17
            };
            this.io.wr(buf, buf.Length, nRead :2);
            this.checkStartupReadback(this.io.exec());

            // === configure GPIOs ===
            buf = new byte[]{
                0x80, // GPIO low
                0x80, // value 
                0x8B, // direction (bit 0: TCK, out; 1: TDI, out; 2: TDO, in; 3: TMS: out; bit 7: Digilent tri-state buffer enable)
                0xAB // invalid command, see http://www.ftdichip.com/Support/Documents/AppNotes/AN_135_MPSSE_Basics.pdf page 17
            };
            this.io.wr(buf, buf.Length, nRead :2);
            this.checkStartupReadback(this.io.exec());
        }

        private void checkStartupReadback(int n) {
            byte[] readback = this.io.getReadCopy(n);

            if(readback.Length != 2) throw new Exception();
            if(readback[0] != 0xFA) throw new Exception("FTDI chip MPSSE error: Expected 'bad opcode' response 0xFA");
            if(readback[1] != 0xAB) throw new Exception("FTDI chip MPSSE error: Expected 'bad opcode' response 0xFA+0xAB");
        }

        /// <summary>
        /// run all commands and return the read number of bytes
        /// </summary>
        public int exec() {
            return this.io.exec();
        }

        public ftdi2_io io;
        private byte[] tmpBuf1 = new byte[1];
        private byte[] tmpBuf3 = new byte[3];

        private enum state_e { unknown = 0, testLogicReset, runTestIdle, shiftDr, shiftIr };
        // current JTAG state
        private state_e state;

        /// <summary>
        /// enters test logic reset state from any other state by 5 consecutive 1s on TMS
        /// </summary>
        public void state_testLogicReset() {
            this.TMS(data :false, nClockCycles :5, tmsLsbFirst :0x1F, tmsFinalState :false);
            this.state = state_e.testLogicReset;
        }

        /// <summary>
        /// enters run-test-idle state
        /// </summary>
        public void state_runTestIdle() {
            switch(this.state) {
                case state_e.testLogicReset:
                    this.TMS(data :false, nClockCycles :1, tmsLsbFirst :0x00, tmsFinalState :false);
                    break;
                case state_e.runTestIdle:
                    return;
                default:
                    throw new Exception("unsupported state transition from "+this.state);
            }
        }

        /// <summary>
        /// enters shift-DR state
        /// </summary>
        public void state_shiftDr() {
            switch(this.state) {
                case state_e.testLogicReset:
                    this.TMS(data :false, nClockCycles :4, tmsLsbFirst :0x02, tmsFinalState :false); // TMS: 0-1-0-0
                    break;
                case state_e.runTestIdle:
                    this.TMS(data :false, nClockCycles :3, tmsLsbFirst :0x01, tmsFinalState :false); // TMS: 1-0-0
                    break;
                default:
                    throw new Exception("unsupported state transition from "+this.state);
            }
            this.state = state_e.shiftDr;
        }

        /// <summary>
        /// enters shift-IR state
        /// </summary>
        public void state_shiftIr() {
            switch(this.state) {
                case state_e.testLogicReset:
                    this.TMS(data :false, nClockCycles :5, tmsLsbFirst :0x06, tmsFinalState :false); // TMS: 0-1-1-0-0
                    break;
                case state_e.runTestIdle:
                    this.TMS(data :false, nClockCycles :4, tmsLsbFirst :0x03, tmsFinalState :false); // TMS: 1-1-0-0
                    break;
                default:
                    throw new Exception("unsupported state transition from "+this.state);
            }
            this.state = state_e.shiftIr;
        }

        /// <summary>
        /// clocks a sequence of bits on the JTAG TMS line
        /// </summary>
        /// <param name="data">state of the data line (constant over all TMS bits)</param>
        /// <param name="nClockCycles">number of cycles on CLK</param>
        /// <param name="tmsLsbFirst">bit sequence on TMS line (clocked out LSB first)</param>
        /// <param name="tmsFinalState">final state of the TMS line. Typically low/false</param>
        private void TMS(bool data, int nClockCycles, byte tmsLsbFirst, bool tmsFinalState) {
            if(nClockCycles > 6) throw new Exception("max number of clock cycles is 6");

            byte val = data ? (byte)0x80 : (byte)0; // state of the data line for the whole length
            val |= tmsLsbFirst;
            if(tmsFinalState)
                val |= (byte)(1 << nClockCycles);
            this.tmpBuf256[0] = 0x4B; // clock TMS bits out
            this.tmpBuf256[1] = (byte)(nClockCycles-1);
            this.tmpBuf256[2] = val;
            this.io.wr(this.tmpBuf256, nWrite :3);
        }

        public void clockN(int nClockCycles) {
            int n2 = nClockCycles >> 3;
            int n1 = nClockCycles - (n2 << 3);

            if(n2 > 0) {
                n2 -= 1;
                this.tmpBuf256[0] = 0x8F; // clock nx8 bits
                this.tmpBuf256[1] = (byte)(n2 & 0xFF); // (n-1) low
                this.tmpBuf256[2] = (byte)((n2 >> 8) & 0xFF); // (n-1) high
                this.io.wr(this.tmpBuf256, nWrite :3);
            }

            if(n1 > 0) {
                n1 -= 1;
                this.tmpBuf256[0] = 0x8E; // clock n bits
                this.tmpBuf256[1] = (byte)n1;
                this.io.wr(this.tmpBuf256, nWrite :2);
            }
        }

        /// <summary>
        /// write bitstream (LSB first)
        /// </summary>
        /// <param name="nBits">number of bits, e.g. nBytes x 8</param>
        /// <param name="data">bytestream</param>
        /// <param name="read">Whether to read back. Returns integer number of bytes.</param>
        public void rwNBits(int nBits, byte[] data, bool read) {
            if(nBits == 0)
                return;
            if(this.state != state_e.shiftDr && this.state != state_e.shiftIr) throw new Exception("unsupported state (need shiftIr or shiftDr, got "+this.state+")");

            nBits -= 1; // final bit raises TMS (special case)
            int nBytes = nBits >> 3;
            nBits &= 0x7;

            // === handle 65k blocks ===
            int nb = nBytes;
            int offset = 0;
            while(nb> 0) {
                int nBlock = Math.Min(nb, 65536);
                // === handle 8-bit chunks ===

                int nLow = (nBlock-1) & 0xFF;
                int nHigh = (nBlock-1) >> 8;
                this.tmpBuf256[0] = (byte)(read ? 0x39 : 0x19); // clock BYTES (and read)
                this.tmpBuf256[1] = (byte)nLow;
                this.tmpBuf256[2] = (byte)nHigh;
                this.io.wr(this.tmpBuf256, nWrite :3);
                this.io.wr(data, nWrite :nBlock, nRead :read ? nBlock : 0, nOffset :offset);

                nb -= nBlock;
                offset += nBlock;
            }

            // === handle remaining bits excluding the final bit ===
            if(nBits > 0) {
                this.tmpBuf256[0] = (byte)(read ? 0x3B : 0x1B); // clock BITS (and read)
                this.tmpBuf256[1] = (byte)(nBits-1);
                this.tmpBuf256[2] = (byte)data[nBytes];
                this.io.wr(this.tmpBuf256, nWrite :3, nRead :read? 1 : 0);
                if(read)
                    this.io.addReadSel(mask :0xFF, shift :8-nBits, mergeWithPrevious :false);
            }

            // === handle final bit in parallel with the first bit of 1-1-0 on TMS to return to IDLE ===
            this.tmpBuf256[0] = (byte)(read ? 0x6B : 0x4B); // clock TMS (and read)
            this.tmpBuf256[1] = 0x02; // length minus one (3 clock cycles)
            this.tmpBuf256[2] = (data[nBytes] & (1 << nBits)) != 0? (byte)0x83 : (byte)0x03; // data (LSB to MSB; first unused bit sets final TMS state; bit 7 sets data state)
            this.io.wr(this.tmpBuf256, nWrite :3, nRead :read? 1 : 0);
            if(read)
                this.io.addReadSel(mask :0x20, shift :5-nBits, mergeWithPrevious :nBits > 0);
            this.state = state_e.runTestIdle;
        }

        /// <summary>
        /// returns the readback result of rwNBits()
        /// </summary>
        /// <returns>read data</returns>
        public byte[] getReadCopy(int n) {
            return this.io.getReadCopy(n);
        }
    }
}