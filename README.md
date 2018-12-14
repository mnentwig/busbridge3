# Busbridge3
Xilinx 7-series FTDI-FPGA interface through JTAG with 125 us roundtrip latency. 

### Hardware:
* FTDI 2232H or 4232H. The H="High-speed" feature is used.
* Xilinx 7-series (Artix). Conceptually proven also on 6-series (Spartan)

### Purpose
* Interfacing of PC to FPGA device through the standard FTDI/JTAG port with minimal latency and high throughput far beyond UART mode
* simple bytestream interface
* protocol-based bus-style interface (built on top of bytestream)
* JTAG bitstream uploader

Compared to a UART, this implementation is based on FTDI's MPSSE mode and achieves ~5x throughput (approaching the FTDI MPSSE hardware limit of 30 MBit/s) and ~5x better latency (in some configurations reaching the theoretical limit of 3 transactions per [USB 2.0 125 us microframe](http://www.usbmadesimple.co.uk/ums_6.htm), that is "ping-pong-ping". However, 125 us is more typical ("ping-pong"). In other words, the interface can quite reliably handle 8000 sequential command-response transactions per second, on some machines approaching 12000. Not surprisingly, a USB hub will increase latency significantly.

Most Xilinx-boards support FTDI-based JTAG in a [standard configuration](https://www.ftdichip.com/Support/Documents/AppNotes/AN_129_FTDI_Hi_Speed_USB_To_JTAG_Example.pdf) with correct pinout for using [MPSSE-mode](https://www.ftdichip.com/Support/Documents/AppNotes/AN_135_MPSSE_Basics.pdf). 

### Do I need it and why not
If a conventional UART will do (hint: use FTDI DLL commands beyond 900 kBaud, not e.g. Windows standard serial port), the answer is clearly NO. Performance is bought by complexity and architectural constraints. Most importantly, the RTL implementation must provide readback data in time, where a UART will simply wait.

### High-level overview
Busbridge3 provides a basic bus interface with address lines, in-/out data, write/read enable and acknowledge for reads. The architecture is flat 32 bit. There are no specific 8-bit legacy features like byte masking, but data widths of 8/16/24/32 bits and arbitrary address increments (0, 1, 2, 3, 4, ...) are supported without loss of throughput.

On the software side, transactions are collected and executed in bulk on demand. For example, many scattered memory writes and reads can be collected to be sent over a single USB frame. The API functions for memory reads return a "handle" to retrieve the data after execution.

The user RTL code must be designed to provide (and acknowledge) readback data in time to be returned with the next JTAG byte. Given the relatively low data rate, this can usually be guaranteed-by-design in the user RTL. 

Optionally, application code can query the remaining number of clock cycles for past reads, and re-schedule them if out of margin (practical if reads are free of side effects and read timeouts are a rare but not impossible event)

### Bytestream interface
The bus-style interface is built on top of a bytestream layer, where bytes sent from the PC are provided to the FPGA fabric without any protocol. An example for standalone use of this mode (which is conceptually close to SPI) is included.

### Bitstream uploader
A .bit file can be uploaded, which e.g. simplifies version management over using flash memory. This feature can be used independently. The bitstream uploader also works for the PL part of Zynq devices if the length of JTAG IR opcodes is zero-padded from 6 to 10 bits (0000 is invalid for the ARM core, which is interpreted as BYPASS).

### Running the demo project / Versions
##### Bitstream compilation
Open _busBridge3_RTL/busBridge3_RTL.xpr_ in _Vivado 2018.1_. Select _PROGRAM AND DEBUG/Generate Bitstream_. Typical build time: 22 + 59 seconds (synthesis/implementation)

##### Hardware installation
Connect a [CMOD A7/35T](https://store.digilentinc.com/cmod-a7-breadboardable-artix-7-fpga-module/) module to USB, disconnect any other Digilent-supported USB devices. If necessary, install FTDI's D2XX drivers (see below).

Other compatible boards should require minimal or no changes, since the example design does not use any pins (clk taken internally from the on-board ring oscillator).

##### Compile: Visual Studio (Microsoft)
* In _Visual Studio 2017_ e.g. Community Edition, open _busmasterSw/busmasterSw.sln_
* Press F5 to build and run (any combination of Debug/Release/Any CPU/x86/x64 should work)
* Note: The project bundles most of the code into a separate DLL for reuse, but this is not necessary (may move all .cs files into the "Console Application" project and delete the DLL project from the solution).

##### Compile: sharpDevelop (GPL)
* In [sharpDevelop 5.1](https://sourceforge.net/projects/sharpdevelop/), open _sharpDevelopBuild/sharpDevelopBuild.sln_
* Press F5 to build and run
* Note: For the time being, this .sln file does not build a separate DLL, like its VS counterpart.

##### Compile: other build tools (e.g. older Visual Studio)
* Create a new Console Application project
* Import all .cs files from _busBridge3_ and _busmasterSw_

##### Validating functionality
Running the C# code shows a console window, and the yellow PROG_DONE LED blinks slowly.

The program continuously sends, receives and verifies data. As long as no exception is thrown, everything should be OK.

##### Porting to a different board / device
Ideally, changing the device in Vivado should be sufficient (the project does not use any LOC-constrained pins). It uses the PROG_DONE LED, which is found on most boards (controlled by software via bit 0 of the example design's register 0x12345678)

### Licenses
* Vivado webpack (no cost) for the FPGA side
* either Visual Studio or [sharpDevelop](https://sourceforge.net/projects/sharpdevelop/) for compilation of the C# code
* FTDI's managed .NET wrapper which is [provided by FTDI](https://www.ftdichip.com/Support/SoftwareExamples/CodeExamples/CSharp.htm) as a "free download" (included by the terms of its own license)
* [FTDI's D2XX drivers](https://www.ftdichip.com/Drivers/D2XX.htm) installed on the machine
* Note, the interface does NOT require a Digilent JTAG license, as it is completely independent (but it does not interfere either)

### How much space does it need?
After stripping off example features (e.g. BRAM), the required resources are minimal, comparable to a UART. 

The design requires one BSCANE2 instantiation (of which there are four in total - in case of conflicts, change the USER ID both in SW and RTL as needed)

### But it doesn't work!
* Is the FTDI chip already opened e.g. by Vivado?
* Does the board support 30 MBit/s between FTDI chip and FPGA? For example, [this FTDI board](https://shop.trenz-electronic.de/en/Products/Trenz-Electronic/Open-Hardware/Xmod-FTDI-JTAG-Adapter/) is limited to 15 MBit/s, most likely because of the CPLD
* Does the board require a specific GPO configuration on the FTDI chip e.g. to enable buffers? The example code is for [CMOD
 A7](https://store.digilentinc.com/cmod-a7-breadboardable-artix-7-fpga-module/), which requires one GPO-bit to enable JTAG buffers.
* Is another FTDI device present? The example code searches for "DIGILENT ADEPT USB DEVICE A" in the FTDI chip's description string (EEPROM). For non-Digilent boards, the string should be edited. Please note, the letter "A" or "B" ("C", "D" for FT4232) is appended by the FTDI chip hardware.
* Electrical problems (USB cable, power supply, microcracks in PCB traces, ...) are not unheard of.

### But the example design has no clock input!?
The example design runs from the FPGA's on-board ring oscillator (~65 MHz) to make it portable, without knowing the board-specific LOCation of the clock pin. DO use a proper crystal-based clock in any "serious" design.

### How can I debug this with Xilinx tools (Microblaze, ILA)?
You can't. The FTDI logical device for JTAG cannot be shared.

If "coexistence" can't be avoided in development, connect another FPGA board through spare GPIOs and instantiate the BSCANE2 on the 2nd board (obviously, not supporting bitstream upload).

### But... why does this need to be so absurdly complex?
Because it's as fast as it goes, using only the standard FTDI/JTAG interface (which may be considered "smallest common denominator" among boards / modules). 

There are a few annoying details that were worked around without losing clock cycles, like splitting off the 8th bit for JTAG state transitions.  

On the bright side: Bitstream upload alone does not need most of the C# code.

### There are no synchronizers in the outbound (application-to-JTAG) clock domain crossing
There are two clock domains:
* The JTAG port (driven by TCK from the FTDI chip, appears in any BSCANE2 primitive)
* The application clock domain at a "higher" frequency (if in doubt, increase the FTDI clock divider to slow things down on the JTAG side)

Outbound data needs to be provided well in time ("guaranteed by design" and possibly checked at runtime via the read margin function). 

If return data arrives so late as to cause metastability, it is invalid in any case. The downstream logic is "robust" so it makes no difference. 

Adding a synchronizer at the slow JTAG frequency would cut heavily into the readback cycle count budget, providing no actual advantage.

The user should review / edit the constraints for a specific application.

The inbound crossing (JTAG to application) is more critical, as it needs to guarantee that parallel events are sampled correctly on a detected toggle event.

At higher application clock frequencies it could make sense to delay the output of the toggle detector (JTAG to application) by an extra application clock cycle.

### Navigating the project
There are three main folders:
* busbridge3_RTL: Verilog code resides here (busBridge3_RTL.srcs\sources_1\top.v). For an own design, modules other than "top()" could remain unchanged. 
* busbridge3: C# code for the driver DLL (possibly import only the release-mode DLL into an own project)
* busmasterSw: Example project and bitstream uploader. Loosely speaking, consider this an example for copy-and-paste into own code.
* sharpDevelop_build: Optional project for sharpDevelop 5.1 (as simple as it gets - no DLL, everything is compiled into the .exe). To recreate the project, set up a new console project, then drag in all .cs files as links.

After cloning from git, first build the RTL project in Vivado for the correct FPGA (default is Artix 7 35 cpg236). Then build and run busmasterSw/busmasterSw.sln. It will upload the bitstream from the RTL folder.

If successful, the PROG_DONE LED will blink at ~0.5 Hz, controlled by software (via bit 0 of the example design's register 0x12345678)

### Slowing down JTAG
Uncomment this line (note, the effective division ratio of the FTDI hardware is clkDiv+1)
```C#
//clkDiv = 10; Console.WriteLine("DEBUG: clkDiv="+clkDiv);
```
Bitstream upload will take longer, and reported read margins should increase.

### Shouldn't I get 480 MBit/s?
Check the *parallel* mode of the FTDI chip on two devices simultaneously (MPSSE is, after all, still serial)
