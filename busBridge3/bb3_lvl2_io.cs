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
                int p = this.readSelBytePos[ix] - nRemoved;
                this.readData[p] &= this.readSelMask[ix];
                if(this.readSelShift[ix] > 0)
                    this.readData[p] >>= this.readSelShift[ix];
                else
                    this.readData[p] <<= -this.readSelShift[ix];
                if(this.readSelMergeWithPrevious[ix]) {
                    this.readData[p-1] |= this.readData[p];
                    ++nRemoved;
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