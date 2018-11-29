// === Scope ==
// bytestream with FTDI chip, hides FT_Write() packet size
using System;
using System.Collections.Generic;
using FTD2XX_busbridge3_NET;
using Debug = System.Diagnostics.Debug;
namespace busbridge3 {
    public class ftdi_io {
        public FTDI dev;
        private int maxTransferSize;
        private byte[] writeData;
        public byte[] readData;
        private byte[] tmpBuf;
        private byte[] tmpBuf256 = new byte[256];
        protected uint nReadPending = 0;
        private uint nWritePending = 0;
        protected int nReadDone = 0;
            
        public byte[] getReadCopy(int n) {
            byte[] val = new byte[n];
            Buffer.BlockCopy(this.readData, 0, val, 0, n);
            return val;
        }

        internal void chk(FTDI.FT_STATUS s) {
            if(s != FTDI.FT_STATUS.FT_OK)
                throw new Exception(s.ToString());
        }

        public virtual int exec() {
            this.tmpBuf256[0] = 0x87; // exec immediately
            this.wr(tmpBuf256,nWrite :1);
            this.flush(finalizeReads :true);

            int nRead = this.nReadDone;
            this.nReadDone = 0;
            Debug.Assert(nReadPending == 0);
            Debug.Assert(nWritePending == 0);
            this.assertNoPendingRxData();
            return nRead;
        }

        private void assertNoPendingRxData() {
            uint n = 0;
            this.dev.GetRxBytesAvailable(ref n);
            if(n > 0)
                throw new Exception("got unhandled data");
        }

        private void flush(bool finalizeReads) {
            uint pW = 0;
            uint nW = this.nWritePending;
            while(nW > 0) { // note: nRead>0 with nWrite==0 is impossible
                // === write chunk ===
                uint nW1 = (uint)Math.Min(nW,maxTransferSize);
#if false
            // === TORTURE TEST ===
            // write single byte
            nW1 = Math.Min(nW1, 1000);
#endif
                uint nW2 = 0;
                if(pW == 0) {
                    this.chk(this.dev.Write(this.writeData,numBytesToWrite :nW1,numBytesWritten :ref nW2));
                }
                else {
                    // copy to temporary buffer with offset
                    Buffer.BlockCopy(src :this.writeData,srcOffset :(int)pW,dst :this.tmpBuf,dstOffset :0,count :(int)nW1);
                    this.chk(this.dev.Write(this.tmpBuf,nW1,ref nW2));
                }

                //if(nW2 != nW1) throw new Exception("write length mismatch (asked for "+nW1+" got "+nW2+")");
                // Note: nW2 != nW1 can be provoked with high clock dividers, e.g. 0x2000, when the chip takes very long to process the data
                nW -= nW2;
                pW += nW2;

                // === read chunk ===
                // ONLY if this flush() closes a transaction:
                //    - Read up to the expected number of read bytes
                //    - block for as long as needed
                // Otherwise, we're in the middle of the Tx bytestream and couldn't know the actual number of read bytes up to the current position
                //    - Take as much data as is available

                uint nR1 = this.nReadPending;
                if((nW > 0) || (finalizeReads == false))
                    this.dev.GetRxBytesAvailable(ref nR1);

                while(nR1 > 0) {

                    // === realloc readMem ===
                    int combinedLength = (int)(this.nReadDone + nR1);
                    if(combinedLength > this.readData.Length) {
                        // heuristic guess => allocate twice the needed memory to manage O(n^2) growth in very long reads
                        // TBD provide some means to reset the memory
                        byte[] tmp = new byte[combinedLength * 2];
                        Buffer.BlockCopy(src :this.readData,srcOffset :0,dst :tmp,dstOffset :0,count :this.nReadDone);
                        this.readData = tmp;
                    }

                    int nR1a = (int)Math.Min(nR1,this.maxTransferSize);
#if false
                // === TORTURE TEST ===
                // read single byte
                nR1a = Math.Min(nR1a, 1);
#endif
                    uint nR2 = 0;

                    //Console.WriteLine(nR1a);
                    if(/*false && */(this.nReadDone == 0)) {
                        this.chk(this.dev.Read(this.readData,(uint)nR1a,ref nR2));
                        if(nR2 == 0)
                            throw new Exception();
                        //if(nR1a != nR2) throw new Exception("read length mismatch (asked for "+nR1a +" got "+nR2 +")");
                    }
                    else {
                        this.chk(this.dev.Read(this.tmpBuf,(uint)nR1a,ref nR2));
                        //if(nR2 == 0) throw new Exception(); // zero read may happen on suspend
                        //if(nR1a != nR2) throw new Exception("read length mismatch (asked for "+nR1a +" got "+nR2 +")");
                        Buffer.BlockCopy(src :this.tmpBuf,srcOffset :0,dst :this.readData,dstOffset :this.nReadDone,count :(int)nR2);
                    }
                    this.nReadDone += (int)nR2;
                    this.nReadPending -= nR2;
                    nR1 -= nR2;
                }
            }
            this.nWritePending = 0;
        }

        public void wr(byte[] data,int nWrite,int nRead = 0,int nOffset = 0) {

#if false
       // === debug: insert "execute immediately" MPSSE token ===
        // THIS DOES NOT WORK (returns 0x87 in read data ?! why?)
        byte[] d2 = new byte[nWrite + 3];
        Buffer.BlockCopy(data, 0, d2, 0, nWrite);
        d2[nWrite] = 0x87;
        d2[nWrite+1] = 0x87;
        d2[nWrite+2] = 0x87;
        nWrite += 3;
        data = d2;
#endif

            this.nReadPending += (uint)nRead;
            int pos = nOffset;
            while(nWrite > 0) {
                int nChunk = Math.Min(this.writeData.Length - (int)this.nWritePending,nWrite);
#if false
            // === TORTURE TEST ===
            // flush single bytes
            nChunk = Math.Min(nChunk, 1);
#endif

                Buffer.BlockCopy(src :data,srcOffset :pos,dst :this.writeData,dstOffset :(int)this.nWritePending,count :nChunk);
                nWrite -= nChunk;
                this.nWritePending += (uint)nChunk;
                if(nWrite == 0)
                    break;
                pos += nChunk;
                this.flush(finalizeReads :false); // reads can only be completed, when the whole bytestream has been written
            }
        }

        public ftdi_io(FTDI dev,int maxTransferSize = 65535,uint timeout_ms = 1000) {
            this.dev = dev;
            this.maxTransferSize = maxTransferSize;
            this.writeData = new byte[maxTransferSize];
            this.readData = new byte[maxTransferSize];
            this.tmpBuf = new byte[maxTransferSize];

            chk(this.dev.ResetDevice());
            chk(this.dev.InTransferSize((uint)maxTransferSize));
            chk(this.dev.SetCharacters(0,false,0,false));
            chk(this.dev.SetTimeouts(timeout_ms,timeout_ms));
            chk(this.dev.SetLatency(0));
            chk(this.dev.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_RTS_CTS,0,0)); // http://www.ftdichip.com/Support/Documents/AppNotes/AN_135_MPSSE_Basics.pdf page 16   
        }
    }
}