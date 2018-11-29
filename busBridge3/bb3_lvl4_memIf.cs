// === SCOPE ===
// implements "busbridge2" interface on top of JTAG for XILINX FPGA via USERx instruction
using System;
using System.Collections.Generic;
using FTD2XX_busbridge3_NET;
namespace busbridge3 {
    public class memIf_cl {
        private ftdi_jtag jtag;
        private byte[] buf = new byte[256];
        private byte[] buf1 = new byte[1];
        private int nBuf = 0;
        private int addrInc = 1; // RTL default
        private int wordwidth = 4; // RTL default
        private int nWords = 1; // RTL default
        private UInt32 addr = 0; // RTL default
        private byte userOpcode;
        private bool readFlag = false;
        /// <summary>possible trailing response bytes overlapping the next command byte</summary>
        private int padTo = 0;
        private UInt32[] buf1UInt32 = new UInt32[1];

        // see busBridge2.v state machine states = command tokens
        private enum cmd_e:byte {
            IDLE = 0,       // NOP. Do nothing, e.g. padding to flush return data
            ADDRINC = 1,    // set address increment state. Note: 0 is valid
            WORDWIDTH = 2,  // set wordwidth in bytes (minus one)
            NWORDS = 3,     // set packet size in words (minus one)
            ADDRWRITE = 4,  // set address then write packet
            WRITE = 5,      // write packet to next address (omit redundant address in message)
            ADDRREAD = 6,   // set address and read
            READ = 7,        // read packet from next address (omit redundant address in message)
            QUERYMARGIN = 8
        }
        public memIf_cl(ftdi_jtag jtag, int user = 1) {
            this.jtag = jtag;
            // JTAG instruction register opcodes for Xilinx BSCANE2 USER register
            switch(user) {
                case 1: this.userOpcode = 0x02; break;
                case 2: this.userOpcode = 0x03; break;
                case 3: this.userOpcode = 0x22; break;
                case 4: this.userOpcode = 0x23; break;
                default:
                    throw new Exception("invalid 'user' argument (supporting USER1..USER4)");
            }
        }

        /// <summary>
        /// Perform all queued write/read operation through FTDI chip and JTAG.
        /// </summary>
        public void exec() {
            for(int ix = this.nBuf; ix < this.padTo; ++ix)
                this.feedUInt8((byte)cmd_e.IDLE);
            this.padTo = 0;

            // === USERx instruction ===
            this.buf1[0] = this.userOpcode;
            this.jtag.state_shiftIr();
            this.jtag.rwNBits(6, this.buf1, false);
            this.jtag.state_shiftDr();
            this.jtag.rwNBits(this.nBuf*8, this.buf, this.readFlag);
            this.nBuf = 0;
            this.readFlag = false;
            this.jtag.exec();
        }

        /// <summary>
        /// check and possibly extend the internal write buffer to accomodate nBytes
        /// </summary>
        /// <param name="nBytes">number of bytes to provide space for</param>
        private void provideBuf(int nBytes) {
            if(buf.Length-this.nBuf < nBytes) {
                int nNew = Math.Max(nBytes, 2*buf.Length);
                byte[] tmp = new byte[nNew];
                Buffer.BlockCopy(this.buf, 0, tmp, 0, this.nBuf);
                this.buf = tmp;
            }
        }

        /// <summary>
        /// Sets the FPGA message decoder to a given address increment
        /// Note: Suppresses redundant messages if the known FPGA state would not change
        /// Note: The busbridge interface is native 32 bit e.g. 0x00000001 is the 2nd full 32-bit word, NOT the 2nd byte of the 1st word.
        /// </summary>
        /// <param name="addrInc">Address increment per word</param>
        private void setAddrInc(int addrInc) {
            if(this.addrInc != addrInc) {
                this.provideBuf(2);
                this.buf[this.nBuf++] = (byte)cmd_e.ADDRINC;
                this.buf[this.nBuf++] = (byte)addrInc;
                this.addrInc = addrInc;
            }
        }

        /// <summary>
        /// Sets the FPGA message decoder to a given wordwidth 
        /// Note: Suppresses redundant messages if the known FPGA state would not change
        /// </summary>
        /// <param name="wordwidth">word width in bytes, e.g. 4 for 32 bit</param>
        private void setWordwidth(int wordwidth) {
            if(this.wordwidth!= wordwidth) {
                this.provideBuf(2);
                this.buf[this.nBuf++] = (byte)cmd_e.WORDWIDTH;
                this.buf[this.nBuf++] = (byte)(wordwidth-1);
                this.wordwidth = wordwidth;
            }
        }

        /// <summary>
        /// Sets number of words to transmit 
        /// </summary>
        /// <param name="nWords">number of words, comprising WORDWIDTH 8-bit bytes each</param>
        private void setNWords(int nWords) {
            if(this.nWords!= nWords) {
                if((nWords == 0) || (nWords > 0xFFFF))
                    throw new Exception("invalid nWords");
                this.nWords = nWords;

                UInt32 nWordsMinusOne = (UInt32)(nWords-1);
                this.provideBuf(3);
                this.buf[this.nBuf++] = (byte)cmd_e.NWORDS;
                this.buf[this.nBuf++] = (byte)((nWordsMinusOne >> 0) & 0xFF);
                this.buf[this.nBuf++] = (byte)((nWordsMinusOne >> 8) & 0xFF);
            }
        }

        /// <summary>
        /// Push an 8-bit word into the internal write buffer to appear at the output of the FPGA's JTAG USERx decoder
        /// </summary>
        /// <param name="val">value to write</param>
        private void feedUInt8(byte val) {
            this.provideBuf(1);
            this.buf[this.nBuf++] = val;
        }

        /// <summary>
        /// Push a 16-bit word into the internal write buffer to appear at the output of the FPGA's JTAG USERx decoder (low byte first)
        /// </summary>
        /// <param name="val">value to write</param>
        private void feedUInt16(UInt16 val) {
            this.provideBuf(2);
            this.buf[this.nBuf++] =                 (byte)((val >> 0) & 0xFF);
            this.buf[this.nBuf++] =                 (byte)((val >> 8) & 0xFF);
        }

        /// <summary>
        /// Push a 32-bit word into the internal write buffer to appear at the output of the FPGA's JTAG USERx decoder (low byte first)
        /// </summary>
        /// <param name="val">value to write</param>
        private void feedUInt32(UInt32 val) {
            this.provideBuf(4);
            this.buf[this.nBuf++] =                 (byte)((val >> 0) & 0xFF);
            this.buf[this.nBuf++] =                 (byte)((val >> 8) & 0xFF);
            this.buf[this.nBuf++] =                 (byte)((val >> 16) & 0xFF);
            this.buf[this.nBuf++] =                 (byte)((val >> 24) & 0xFF);
        }

        /// <summary>
        /// Push a 32-bit word into the internal write buffer to appear at the output of the FPGA's JTAG USERx decoder (low byte first)
        /// </summary>
        /// <param name="val">value to write</param>
        private void feedInt32(Int32 val) {
            this.provideBuf(4);
            this.buf[this.nBuf++] =                 (byte)((val >> 0) & 0xFF);
            this.buf[this.nBuf++] =                 (byte)((val >> 8) & 0xFF);
            this.buf[this.nBuf++] =                 (byte)((val >> 16) & 0xFF);
            this.buf[this.nBuf++] =                 (byte)((val >> 24) & 0xFF);
        }

        /// <summary>
        /// Helper function to set up a write transaction
        /// </summary>
        /// <param name="wordwidth">Use this wordwidth for transmission</param>
        /// <param name="addr">Transmit to this address</param>
        /// <param name="addrInc">Address increment per word</param>
        /// <param name="nWords">Number of words to write</param>
        private void writeHeaderAdvanceAddress(int wordwidth, UInt32 addr, int addrInc, int nWords) {
            // === configure persistent state ===
            // note: addrInc does not matter for single-word transactions
            if(nWords != 1)
                this.setAddrInc(addrInc);
            this.setWordwidth(wordwidth);
            this.setNWords(nWords);
            if(addr == this.addr) {
                // === send WRITE command (HW address pointer is correct) ===
                this.feedUInt8((byte)cmd_e.WRITE);
            } else {
                // === send ADDRWRITE command (HW address pointer needs to be set) ===
                this.feedUInt8((byte)cmd_e.ADDRWRITE);
                this.feedUInt32(addr); // parameter to ADDRWRITE
                this.addr = addr;
            }

            // === advance address to HW state at end of operation ===
            unchecked {
                this.addr += (UInt32)(this.addrInc * nWords);
            }
        }

        /// <summary>
        /// 8-bit write
        /// </summary>
        /// <param name="addr">destination address</param>
        /// <param name="data">data to write</param>
        /// <param name="offset">offset into data (default: 0)</param>
        /// <param name="n">number of items to write (default: to end)</param>
        /// <param name="addrInc">address increase on target platform</param>
        public void write(UInt32 addr, byte[] data, int offset = 0, int n = -1, int addrInc = 1) {
            if(n < 0)
                n = data.Length - offset;

            this.writeHeaderAdvanceAddress(wordwidth :1, addr :addr, addrInc :addrInc, nWords :n);

            while(n-- > 0)
                this.feedUInt8(data[offset++]);
        }

        /// <summary>
        /// 16-bit write
        /// </summary>
        /// <param name="addr">destination address</param>
        /// <param name="data">data to write</param>
        /// <param name="offset">offset into data (default: 0)</param>
        /// <param name="n">number of items to write (default: to end)</param>
        /// <param name="addrInc">address increase on target platform</param>
        public void write(UInt32 addr, UInt16[] data, int offset = 0, int n = -1, int addrInc = 1) {
            if(n < 0)
                n = data.Length - offset;

            this.writeHeaderAdvanceAddress(wordwidth :2, addr :addr, addrInc :addrInc, nWords :n);

            while(n-- > 0)
                this.feedUInt16(data[offset++]);
        }

        public void write(UInt32 addr, UInt32 data) {
            this.buf1UInt32[0] = data;
            this.write(addr, this.buf1UInt32, offset :0, n :1);
        }

        /// <summary>
        /// 32-bit write
        /// </summary>
        /// <param name="addr">destination address</param>
        /// <param name="data">data to write</param>
        /// <param name="offset">offset into data (default: 0)</param>
        /// <param name="n">number of items to write (default: to end)</param>
        /// <param name="addrInc">address increase on target platform</param>
        public void write(UInt32 addr, UInt32[] data, int offset = 0, int n = -1, int addrInc = 1) {
            if(n < 0)
                n = data.Length - offset;
            this.writeHeaderAdvanceAddress(wordwidth :4, addr :addr, addrInc :addrInc, nWords :n);
            while(n-- > 0)
                this.feedUInt32(data[offset++]);
        }

        /// <summary>
        /// 32-bit write
        /// </summary>
        /// <param name="addr">destination address</param>
        /// <param name="data">data to write</param>
        /// <param name="offset">offset into data (default: 0)</param>
        /// <param name="n">number of items to write (default: to end)</param>
        /// <param name="addrInc">address increase on target platform</param>
        public void write(UInt32 addr, Int32[] data, int offset = 0, int n = -1, int addrInc = 1) {
            if(n < 0)
                n = data.Length - offset;
            this.writeHeaderAdvanceAddress(wordwidth :4, addr :addr, addrInc :addrInc, nWords :n);
            while(n-- > 0)
                this.feedInt32(data[offset++]);
        }

        /// <summary>
        /// Helper function to set up a read transaction
        /// </summary>
        /// <param name="wordwidth">Use this wordwidth for reception</param>
        /// <param name="addr">Read from this address</param>
        /// <param name="addrInc">Address increment per word</param>
        /// <param name="nWords">Number of words to read</param>
        private void readHeaderAdvanceAddress(int wordwidth, UInt32 addr, int addrInc, int nWords) {
            // === configure persistent state ===
            // note: addrInc does not matter for single-word transactions
            if (nWords != 1)
                this.setAddrInc(addrInc);
            this.setWordwidth(wordwidth);
            this.setNWords(nWords);

            if(addr == this.addr) {
                // === send READ command (HW address pointer is correct) ===
                this.feedUInt8((byte)cmd_e.READ);
            } else {
                // === send ADDRREAD command (HW address pointer needs to be set) ===
                this.feedUInt8((byte)cmd_e.ADDRREAD);
                this.feedUInt32(addr); // parameter to ADDRREAD
                this.addr = addr;
            }
            // === advance address to HW state at end of operation ===
            unchecked {
                this.addr += (UInt32)(this.addrInc * nWords);
            }
        }

        public int readUInt32(UInt32 addr, int nWords = 1, int addrInc = 1) {
            this.readHeaderAdvanceAddress(wordwidth :4, addr :addr, addrInc :addrInc, nWords :nWords);
            const int off = 1; // hardware delay in bytes
            int retVal = this.nBuf+off;
            for(int ix = 0; ix < nWords; ++ix) {
                this.feedUInt32(0);
            }
            this.readFlag = true;
            this.padTo = this.nBuf + off;
            return retVal;
        }

        public int queryMargin() {
            this.provideBuf(3);
            this.buf[this.nBuf++] = (byte)cmd_e.QUERYMARGIN;

            const int off = 1; // hardware delay in bytes
            int retVal = this.nBuf+off;
            this.buf[this.nBuf++] = (byte)0;
            this.buf[this.nBuf++] = (byte)0;

            this.readFlag = true;
            this.padTo = this.nBuf + off;
            return retVal;
        }

        public int readUInt16(UInt32 addr, int nWords = 1, int addrInc = 1) {
            this.readHeaderAdvanceAddress(wordwidth :2, addr :addr, addrInc :addrInc, nWords :nWords);
            const int off = 1; // hardware delay in bytes
            int retVal = this.nBuf+off;
            for(int ix = 0; ix < nWords; ++ix) {
                this.feedUInt16(0);
            }
            this.readFlag = true;
            this.padTo = this.nBuf + off;
            return retVal;
        }

        public int readUInt8(UInt32 addr, int nWords = 1, int addrInc = 1) {
            this.readHeaderAdvanceAddress(wordwidth :1, addr :addr, addrInc :addrInc, nWords :nWords);
            int off = 1;
            int retVal = this.nBuf+off;
            for(int ix = 0; ix < nWords; ++ix) {
                this.feedUInt8(0);
            }
            this.readFlag = true;
            this.padTo = this.nBuf + off;
            return retVal;
        }

        public UInt32 getUInt32(int offset) {
            UInt32 retVal;
            retVal = (UInt32)this.jtag.io.readData[offset++];
            retVal |= (UInt32)(this.jtag.io.readData[offset++] << 8);
            retVal |= (UInt32)(this.jtag.io.readData[offset++] << 16);
            retVal |= (UInt32)(this.jtag.io.readData[offset++] << 24);
            return retVal;
        }

        public UInt32[] getUInt32(int offset, int num) {
            UInt32[] r = new UInt32[num];
            for(int ix = 0; ix < num; ++ix) {
                r[ix] = this.getUInt32(offset);
                offset += 4;
            }
            return r;
        }

        public UInt16 getUInt16(int offset) {
            UInt16 retVal;
            retVal = (UInt16)this.jtag.io.readData[offset++];
            retVal |= (UInt16)(this.jtag.io.readData[offset++] << 8);
            return retVal;
        }

        public UInt16[] getUInt16(int offset, int num) {
            UInt16[] r = new UInt16[num];
            for(int ix = 0; ix < num; ++ix) {
                r[ix] = this.getUInt16(offset);
                offset += 2;
            }
            return r;
        }

        public byte getUInt8(int offset) {
            return this.jtag.io.readData[offset];
        }

        public byte[] getUInt8(int offset, int num) {
            byte[] retVal = new byte[num];
            for(int ix = 0; ix < num; ++ix)
                retVal[ix] = this.jtag.io.readData[offset++];
            return retVal;
        }

        // === memtest utility functions ===

        static Random RNG = new Random(Seed :0);
        static void FisherYatesShuffle<T>(IList<T> list) {
            int n = list.Count;
            while(n > 1) {
                n--;
                int k = RNG.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        private static void segmentizeMemTest(int memSize, List<uint> segmentAddr, List<int> segmentLen) {
            List<UInt32> segN = new List<UInt32>();

            // === divide into random segments ===
            segN.Clear();
            int nRem = memSize;
            while(nRem > 0) {
                UInt32 n = (UInt32)RNG.Next(nRem)+1;
                segN.Add(n);
                nRem -= (int)n;
            }
            FisherYatesShuffle<UInt32>(segN);
            segmentAddr.Clear();
            segmentLen.Clear();
            UInt32 addr = 0;
            foreach(int n in segN) {
                segmentLen.Add(n);
                segmentAddr.Add(addr);
                addr += (UInt32)n;
            }        
        }

        public void memTest32(int memSize, UInt32 baseAddr, int nIter) {
            UInt32[] memWrite = new UInt32[memSize];
            UInt32[] memRead = new UInt32[memSize];
            byte[] tmp = new byte[Buffer.ByteLength(memWrite)];
            List<uint> segmentAddr = new List<uint>();
            List<int> segmentLen = new List<int>();
            List<int> readHandle = new List<int>();
            List<int> segOrder = new List<int>();
            while(nIter-- > 0) {
                readHandle.Clear();

                // === create random data ===
                RNG.NextBytes(tmp);
                Buffer.BlockCopy(tmp, 0, memWrite, 0, Buffer.ByteLength(memWrite));

#if false
      // === debug: force readable data ===
            for(int ix = 0; ix < memWrite.Length; ++ix) 
                memWrite[ix] = (UInt32)ix+1;
#endif
                segmentizeMemTest(memSize, segmentAddr, segmentLen);

                // === set up randomized write order ===
                segOrder.Clear();
                for(int ix = 0; ix < segmentAddr.Count; ++ix)
                    segOrder.Add(ix);
                FisherYatesShuffle<int>(segOrder);

                // === schedule write operations ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    this.write(addr :baseAddr + segmentAddr[seg], data :memWrite, offset :(int)segmentAddr[seg], n :segmentLen[seg], addrInc :1);
                }

                // === set up randomized read order ===
                FisherYatesShuffle<int>(segOrder);
         
                // === schedule read operations, store handle ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    readHandle.Add(this.readUInt32(addr :baseAddr + segmentAddr[seg], nWords :segmentLen[seg], addrInc :1));
                }

                // === send to hardware ===
                this.exec();

                // === get readback result ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    UInt32[] chunk = this.getUInt32(offset :readHandle[ix], num :segmentLen[seg]);
                    Array.Copy(chunk, 0, memRead, segmentAddr[seg], segmentLen[seg]);
                }

                // === compare ===
                for(int ix = 0; ix < memWrite.Length; ++ix) {
                    if(memRead[ix] != memWrite[ix])
                        throw new Exception(String.Format("memtest 32: data differs. Expected 0x{0:X} got 0x{1:X}", memWrite[ix], memRead[ix]));
                }
            }
        }

        public void memTest16(int memSize, UInt32 baseAddr, int nIter) {
            UInt16[] memWrite = new UInt16[memSize];
            UInt16[] memRead = new UInt16[memSize];
            byte[] tmp = new byte[Buffer.ByteLength(memWrite)];
            List<uint> segmentAddr = new List<uint>();
            List<int> segmentLen = new List<int>();
            List<int> readHandle = new List<int>();
            List<UInt32> segN = new List<UInt32>();
            List<int> segOrder = new List<int>();
            while(nIter-- > 0) {
                readHandle.Clear();

#if true
                // === create random data ===
                RNG.NextBytes(tmp);
                Buffer.BlockCopy(tmp, 0, memWrite, 0, Buffer.ByteLength(memWrite));

                segmentizeMemTest(memSize, segmentAddr, segmentLen);

                // === set up randomized write order ===
                segOrder.Clear();
                for(int ix = 0; ix < segmentAddr.Count; ++ix)
                    segOrder.Add(ix);
                FisherYatesShuffle<int>(segOrder);
#else
                // === debug: force readable data ===
                for(int ix = 0; ix < memWrite.Length; ++ix)
                    memWrite[ix] = (UInt16)(ix+1);
                segmentLen.Add(memSize);
                segmentAddr.Add(0);
                segOrder.Add(0);
#endif
                // === schedule write operations ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    this.write(addr :baseAddr + segmentAddr[seg], data :memWrite, offset :(int)segmentAddr[seg], n :segmentLen[seg], addrInc :1);
                }

                // === set up randomized read order ===
                FisherYatesShuffle<int>(segOrder);

                // === schedule read operations, store handle ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    readHandle.Add(this.readUInt16(addr :baseAddr + segmentAddr[seg], nWords :segmentLen[seg], addrInc :1));
                }

                // === send to hardware ===
                this.exec();

                // === get readback result ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    UInt16[] chunk = this.getUInt16(offset :readHandle[ix], num :segmentLen[seg]);
                    Array.Copy(chunk, 0, memRead, segmentAddr[seg], segmentLen[seg]);
                }

                // === compare ===
                for(int ix = 0; ix < memWrite.Length; ++ix) {
                    if(memRead[ix] != memWrite[ix])
                        throw new Exception(String.Format("memtest 16: data differs. Expected 0x{0:X} got 0x{1:X}", memWrite[ix], memRead[ix]));
                }
            }
        }

        public void memTest8(int memSize, UInt32 baseAddr, int nIter) {
            byte[] memWrite = new byte[memSize];
            byte[] memRead = new byte[memSize];
            byte[] tmp = new byte[Buffer.ByteLength(memWrite)];
            List<uint> segmentAddr = new List<uint>();
            List<int> segmentLen = new List<int>();
            List<int> readHandle = new List<int>();
            List<UInt32> segN = new List<UInt32>();
            List<int> segOrder = new List<int>();
            while(nIter-- > 0) {
                readHandle.Clear();

                // === create random data ===
                RNG.NextBytes(tmp);
                Buffer.BlockCopy(tmp, 0, memWrite, 0, Buffer.ByteLength(memWrite));

#if false
      // === debug: force readable data ===
            for(int ix = 0; ix < memWrite.Length; ++ix) 
                memWrite[ix] = (UInt32)ix+1;
#endif
                segmentizeMemTest(memSize, segmentAddr, segmentLen);

                // === set up randomized write order ===
                segOrder.Clear();
                for(int ix = 0; ix < segmentAddr.Count; ++ix)
                    segOrder.Add(ix);
                FisherYatesShuffle<int>(segOrder);

                // === schedule write operations ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    this.write(addr :baseAddr + segmentAddr[seg], data :memWrite, offset :(int)segmentAddr[seg], n :segmentLen[seg], addrInc :1);
                }

                // === set up randomized read order ===
                FisherYatesShuffle<int>(segOrder);

                // === schedule read operations, store handle ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    readHandle.Add(this.readUInt8(addr :baseAddr + segmentAddr[seg], nWords :segmentLen[seg], addrInc :1));
                }

                // === send to hardware ===
                this.exec();

                // === get readback result ===
                for(int ix = 0; ix < segOrder.Count; ++ix) {
                    int seg = segOrder[ix];
                    byte[] chunk = this.getUInt8(offset :readHandle[ix], num :segmentLen[seg]);
                    Array.Copy(chunk, 0, memRead, segmentAddr[seg], segmentLen[seg]);
                }

                // === compare ===
                for(int ix = 0; ix < memWrite.Length; ++ix) {
                    if(memRead[ix] != memWrite[ix])
                        throw new Exception(String.Format("memtest 8: data differs. Expected 0x{0:X} got 0x{1:X}", memWrite[ix], memRead[ix]));
                }
            }
        }
    }
}