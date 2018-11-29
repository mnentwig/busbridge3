// === scope ===
// helper class on top of ftdi_io to merge reads for bit numbers that are not a multiple of 8
// (e.g. the final bit with TMS=1 when exiting JTAG SHIFT-DR state)
using System;
using System.Collections.Generic;
using FTD2XX_busbridge3_NET;

namespace busbridge3 {
    public class ftdi2_io:ftdi_io {
        public ftdi2_io(FTDI dev, int maxTransferSize = 65535, uint timeout_ms = 1000):base(dev:dev, maxTransferSize:maxTransferSize, timeout_ms:timeout_ms) { }

        private List<int> readSelBytePos = new List<int>();
        private List<byte> readSelMask = new List<byte>();
        private List<int> readSelShift = new List<int>();
        private List<bool> readSelMergeWithPrevious = new List<bool>();

        /// <summary>Register a required modification on the readback data (combine a split bit with TMS change)</summary>
        /// <param name="mask">mask to apply on a readback byte</param>
        /// <param name="shift">bit shift to apply on a readback byte (positive: right; negative: left; 0: no shift)</param>
        /// <param name="mergeWithPrevious">if set, merges a byte into the previous byte and shortens the stream by 1</param>
        public void addReadSel(byte mask, int shift, bool mergeWithPrevious) {
            int nextReadPosInOutMem = (int)(this.nReadPending+this.nReadDone);
            this.readSelBytePos.Add(nextReadPosInOutMem-1);
            this.readSelMask.Add(mask);
            this.readSelShift.Add(shift);
            this.readSelMergeWithPrevious.Add(mergeWithPrevious);
        }

        public override int exec() {
            // === get raw bytestream ===
            int nRead = base.exec();

            // === apply modifications ===
            int nRemoved = 0;
            int nReadSel = this.readSelBytePos.Count;
            for(int ix = 0; ix < nReadSel; ++ix) {
                int p = this.readSelBytePos[ix] - nRemoved; // must account for the bytes that were already removed
                this.readData[p] &= this.readSelMask[ix];
                if(this.readSelShift[ix] > 0)
                    this.readData[p] >>= this.readSelShift[ix];
                else
                    this.readData[p] <<= -this.readSelShift[ix];
                if(this.readSelMergeWithPrevious[ix]) {
                    // === merge with previous ===
                    this.readData[p-1] |= this.readData[p];
                    
                    // === remove merged partial byte ===
                    --nRead;
                    ++nRemoved;

                    // === shift remaining data into the gap ===
                    for(int ix2 = p;ix2 < nRead;++ix2)
                        this.readData[ix2] = this.readData[ix2+1];
                }
            }

            // === clean up ===
            this.readSelBytePos.Clear();
            this.readSelMergeWithPrevious.Clear();
            this.readSelMask.Clear();
            this.readSelShift.Clear();
            return nRead;
        }
    }
}