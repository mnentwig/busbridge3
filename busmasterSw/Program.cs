using System;
using FTD2XX_busbridge3_NET; // modified FTDI C# wrapper
using busbridge3;

class Program {
    static void Main(string[] args) {
        try {
            Main2(args);
        }
        catch(Exception e) {
            Console.WriteLine("unhandled exception ");
            Console.WriteLine(e.Message);
            Console.WriteLine("press RETURN to exit");
            Console.ReadLine();
        }
    }
    static void Main2(string[] args) {
        System.Diagnostics.Stopwatch sw2 = new System.Diagnostics.Stopwatch();

        // device identification from the FTDI chip's EEPROM
        // as reported by FTPROG utility
        // note: DO NOT use FTPROG to write to Digilent devices, it will overwrite the license key for Xilinx tools
        string devSearchString = "DIGILENT ADEPT USB DEVICE A";

        // === identify suitable FTDI device ===
        print("Enumerating FTDI devices..."); sw2.Reset(); sw2.Start();
        FTDI myFtdiDevice = new FTDI();
        UInt32 n = 0;
        FTDI.FT_STATUS s = myFtdiDevice.GetNumberOfDevices(ref n); chk(s);
        if(n == 0) throw new Exception("no FTDI devices");
        FTDI.FT_DEVICE_INFO_NODE[] ftdiDeviceList = new FTDI.FT_DEVICE_INFO_NODE[n];
        s = myFtdiDevice.GetDeviceList(ftdiDeviceList); chk(s);
        printLine(sw2.ElapsedMilliseconds+" ms");

        print("Scanning FTDI devices for name '"+devSearchString+"'..."); sw2.Reset(); sw2.Start();
        int ixDev = -1;
        for(UInt32 i = 0;i < n;i++) {
            if(ftdiDeviceList[i] == null)
                continue;
            if(ftdiDeviceList[i].Description.ToUpper().Contains(devSearchString))
                ixDev = (int)i;
        }
        if(ixDev < 0)
            throw new Exception("No suitable FTDI device found\nHint: is the device already claimed by a running instance of this program?\n");
        printLine(sw2.ElapsedMilliseconds+" ms");

        // === open FTDI device ===
        print("Opening device..."); sw2.Reset(); sw2.Start();
        s = myFtdiDevice.OpenBySerialNumber(ftdiDeviceList[ixDev].SerialNumber); chk(s);
        printLine(sw2.ElapsedMilliseconds+" ms");

        // === create FTDI MPSSE-level IO object ===
        ftdi2_io io = new ftdi2_io(myFtdiDevice,maxTransferSize: 65535);

        // === create JTAG-level IO ===
        print("getting IDCODE..."); sw2.Reset(); sw2.Start();
        uint clkDiv = 0;
        //clkDiv = 10; Console.WriteLine("DEBUG: clkDiv="+clkDiv);
        ftdi_jtag jtag = new ftdi_jtag(io,clkDiv: clkDiv);

        jtag.state_testLogicReset();
        jtag.state_shiftIr();
        byte[] bufa = new byte[] { 0x09 };
        jtag.rwNBits(6,bufa,false);

#if true
        // === testcase for JTAG read splitting, where the final bit needs a separate command to set TMS ===
        // Repeat cycling through the JTAG state machine and read IDCODE. At FTDI driver level, this is fairly complex 
        // since the final bit with TMS = 1 needs to be split off into a separate command, returning a separate byte
        int nRepRead = 20;
        for(int nBits = 25;nBits <= 32;++nBits) { // exercise all combinations that return four bytes (different command patterns at FTDI opcode level)
            for(int ix = 0;ix < nRepRead;++ix) {
                // === get IDCODE ===
                // https://www.xilinx.com/support/documentation/user_guides/ug470_7Series_Config.pdf page 173 IDCODE == 0b001001
                bufa = new byte[4];
                jtag.state_shiftDr();
                jtag.rwNBits(nBits,bufa,true);
            }
            bufa = jtag.getReadCopy(jtag.exec());
            if(bufa.Length != nRepRead * 4)
                throw new Exception();
            for(int ix = 1;ix < nRepRead;++ix) {
                if((bufa[4*ix-4] != bufa[4*ix]) || (bufa[4*ix-4+1] != bufa[4*ix+1]) || (bufa[4*ix-4+2] != bufa[4*ix+2]) || (bufa[4*ix-4+3] != bufa[4*ix+3]))
                    throw new Exception();
            }
        }
#endif

        // === get IDCODE ===
        // https://www.xilinx.com/support/documentation/user_guides/ug470_7Series_Config.pdf page 173 IDCODE == 0b001001
        bufa = new byte[4];
        jtag.state_shiftDr();
        jtag.rwNBits(32,bufa,true);
        bufa = jtag.getReadCopy(jtag.exec());
        bufa[3] &= 0x0F; // mask out revision bytes
        UInt64 idCode = ((UInt64)bufa[3] << 24) | ((UInt64)bufa[2] << 16) | ((UInt64)bufa[1] << 8) | (UInt64)bufa[0];
        printLine(sw2.ElapsedMilliseconds+" ms");
        Console.WriteLine("IDCODE {0:X8}",idCode);

        // === determine FPGA ===
        // https://www.xilinx.com/support/documentation/user_guides/ug470_7Series_Config.pdf page 14
        string bitstreamFile;
        switch(idCode) {
            case 0x362E093: bitstreamFile = "XC7A15T.bit"; break;
            case 0x362D093: bitstreamFile = "XC7A35T.bit"; break;
            case 0x362C093: bitstreamFile = "XC7A50T.bit"; break;
            case 0x3632093: bitstreamFile = "XC7A75T.bit"; break;
            case 0x3631093: bitstreamFile = "XC7A100T.bit"; break;
            case 0x3636093: bitstreamFile = "XC7A200T.bit"; break;
            default: throw new Exception(String.Format("unsupported FPGA (unknown IDCODE 0x{0:X7})",idCode));
        }

        bitstreamFile = @"..\..\..\busBridge3_RTL\busBridge3_RTL.runs\impl_1\top.bit"; Console.WriteLine("DEBUG: Trying to open bitstream from "+bitstreamFile);
        byte[] bitstream = System.IO.File.ReadAllBytes(bitstreamFile);

        // === upload to FPGA ===
        sw2.Reset(); sw2.Start();
        uploadBitstream(jtag,bitstream);
        Console.WriteLine("bitstream upload: "+sw2.ElapsedMilliseconds+" ms");
#if false
        // === exercises the USERx opcode ===
        // use this as template to work with user circuitry that is directly attached to the BSCANE2 component (without using the "higher" busbridge layers)
        byte[] tmp = new byte[32];
        for(int ix = 0;ix < tmp.Length;++ix)
            tmp[ix] = (byte)ix;

        // === USERx instruction ===
        byte[] buf1 = new byte[] { 0x02 }; // USER1 opcode
        jtag.state_shiftIr();
        jtag.rwNBits(6,buf1,false);
        jtag.state_shiftDr();
        jtag.rwNBits(tmp.Length*8,tmp,true);
        jtag.exec();
        byte[] bRead = jtag.getReadCopy(tmp.Length);
        foreach(byte b in bRead)
            Console.WriteLine(String.Format("{0:X2}",b));
#endif

        // === open memory interface ===
        memIf_cl m = new memIf_cl(jtag);
#if false
        int h = m.readUInt32(addr: 0x12345678,nWords: 2,addrInc: 1);
        m.exec();
        uint num = m.getUInt32(h);
        Console.WriteLine(num);
#endif

        // === self test ===
        for(long count = 0;count > -1;++count) {
            int memSize = 16384;
            uint ram = 0xF0000000;

            sw2.Reset(); sw2.Start();
            int nRep = 1000;
            m.memTest32(memSize: 1,baseAddr: 0x87654321,nIter: nRep);
            Console.WriteLine("roundtrip time "+((double)sw2.ElapsedMilliseconds/(double)nRep)+" ms (should be close to 0.125 ms from 8 kHz USB 2.0 microframe rate)");

            m.memTest8(memSize: memSize,baseAddr: ram,nIter: 40);
            m.memTest16(memSize: memSize,baseAddr: ram,nIter: 20);
            m.memTest32(memSize: memSize,baseAddr: ram,nIter: 10);
            m.write(addr: 0x12345678,data: (uint)count&1);
            m.queryMargin(); // reset timing margin tracker

            // execute read and check margin
            m.readUInt32(addr: 0xF0000000);
            int handle = m.queryMargin();

            // execute read and check margin
            m.readUInt32(addr: 0x12345678);
            int handle2 = m.queryMargin();

            // configure test register delay, read and check margin
            UInt32 regVarReadLen = 0x98765432;
            m.write(addr: regVarReadLen,data: 14);
            int h0 = m.readUInt32(addr: regVarReadLen);
            int handle3 = m.queryMargin();

            m.exec(); // all transactions to hardware

            UInt16 margin = m.getUInt16(handle);
            Console.WriteLine("margin 1: " + margin);
            UInt16 margin2 = m.getUInt16(handle2);
            Console.WriteLine("margin 2: " +margin2);
            UInt16 margin3 = m.getUInt16(handle3);
            UInt16 m3 = m.getUInt16(h0);
            Console.WriteLine("configured test register delay: " +m3+" remaining margin: "+margin3);
        }
        Console.WriteLine("All tests passed. Press RETURN to close");
        Console.ReadLine();
    }

    static void uploadBitstream(ftdi_jtag jtag,byte[] buf) {
        byte[] bitReverse = {
    0x00, 0x80, 0x40, 0xc0, 0x20, 0xa0, 0x60, 0xe0,
    0x10, 0x90, 0x50, 0xd0, 0x30, 0xb0, 0x70, 0xf0,
    0x08, 0x88, 0x48, 0xc8, 0x28, 0xa8, 0x68, 0xe8,
    0x18, 0x98, 0x58, 0xd8, 0x38, 0xb8, 0x78, 0xf8,
    0x04, 0x84, 0x44, 0xc4, 0x24, 0xa4, 0x64, 0xe4,
    0x14, 0x94, 0x54, 0xd4, 0x34, 0xb4, 0x74, 0xf4,
    0x0c, 0x8c, 0x4c, 0xcc, 0x2c, 0xac, 0x6c, 0xec,
    0x1c, 0x9c, 0x5c, 0xdc, 0x3c, 0xbc, 0x7c, 0xfc,
    0x02, 0x82, 0x42, 0xc2, 0x22, 0xa2, 0x62, 0xe2,
    0x12, 0x92, 0x52, 0xd2, 0x32, 0xb2, 0x72, 0xf2,
    0x0a, 0x8a, 0x4a, 0xca, 0x2a, 0xaa, 0x6a, 0xea,
    0x1a, 0x9a, 0x5a, 0xda, 0x3a, 0xba, 0x7a, 0xfa,
    0x06, 0x86, 0x46, 0xc6, 0x26, 0xa6, 0x66, 0xe6,
    0x16, 0x96, 0x56, 0xd6, 0x36, 0xb6, 0x76, 0xf6,
    0x0e, 0x8e, 0x4e, 0xce, 0x2e, 0xae, 0x6e, 0xee,
    0x1e, 0x9e, 0x5e, 0xde, 0x3e, 0xbe, 0x7e, 0xfe,
    0x01, 0x81, 0x41, 0xc1, 0x21, 0xa1, 0x61, 0xe1,
    0x11, 0x91, 0x51, 0xd1, 0x31, 0xb1, 0x71, 0xf1,
    0x09, 0x89, 0x49, 0xc9, 0x29, 0xa9, 0x69, 0xe9,
    0x19, 0x99, 0x59, 0xd9, 0x39, 0xb9, 0x79, 0xf9,
    0x05, 0x85, 0x45, 0xc5, 0x25, 0xa5, 0x65, 0xe5,
    0x15, 0x95, 0x55, 0xd5, 0x35, 0xb5, 0x75, 0xf5,
    0x0d, 0x8d, 0x4d, 0xcd, 0x2d, 0xad, 0x6d, 0xed,
    0x1d, 0x9d, 0x5d, 0xdd, 0x3d, 0xbd, 0x7d, 0xfd,
    0x03, 0x83, 0x43, 0xc3, 0x23, 0xa3, 0x63, 0xe3,
    0x13, 0x93, 0x53, 0xd3, 0x33, 0xb3, 0x73, 0xf3,
    0x0b, 0x8b, 0x4b, 0xcb, 0x2b, 0xab, 0x6b, 0xeb,
    0x1b, 0x9b, 0x5b, 0xdb, 0x3b, 0xbb, 0x7b, 0xfb,
    0x07, 0x87, 0x47, 0xc7, 0x27, 0xa7, 0x67, 0xe7,
    0x17, 0x97, 0x57, 0xd7, 0x37, 0xb7, 0x77, 0xf7,
    0x0f, 0x8f, 0x4f, 0xcf, 0x2f, 0xaf, 0x6f, 0xef,
    0x1f, 0x9f, 0x5f, 0xdf, 0x3f, 0xbf, 0x7f, 0xff
                            };

        // === bit reverse data ===
        for(int ix = 0;ix < buf.Length;++ix) {
            buf[ix] = bitReverse[buf[ix]];
        }

        byte[] b1 = new byte[1];

        // === enter TLR reset state ===
        jtag.state_testLogicReset();

        // === issue SHUTDOWN command ===
        b1[0] = 0x0d;
        jtag.state_shiftIr();
        jtag.rwNBits(6,b1,false);

        jtag.clockN(16);

        // === issue CFG_IN command ===
        b1[0] = 0x05;
        jtag.state_shiftIr();
        jtag.rwNBits(6,b1,false);

        jtag.state_shiftDr();
        jtag.rwNBits(buf.Length*8,buf,false);

        jtag.clockN(1);

        // === issue JSTART command ===
        b1[0] = 0x0c;
        jtag.state_shiftIr();
        jtag.rwNBits(6,b1,false);

        jtag.clockN(32);
        jtag.exec();
    }

    static void chk(FTDI.FT_STATUS s) {
        if(s != FTDI.FT_STATUS.FT_OK)
            throw new Exception(s.ToString());
    }
    static void print(string msg) {
        Console.Write(msg);
        Console.OpenStandardOutput().Flush();
    }

    static void printLine(string msg) {
        Console.WriteLine(msg);
    }
}
