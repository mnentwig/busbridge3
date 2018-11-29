# Busbridge3
Xilinx 7-series FTDI-FPGA interface through JTAG with 125 us roundtrip latency. 

# Hardware:
* FTDI 2232H or 4232H. The H="High-speed" feature is used.
* Xilinx 7-series (Artix). Conceptually proven also on 6-series (Spartan)

# Purpose
Interfacing of PC to FPGA device with
* minimal latency
* high throughput
* bitstream uploader (can be used independently)

Most Xilinx-boards support FTDI-based JTAG in a [standard configuration](https://www.ftdichip.com/Support/Documents/AppNotes/AN_129_FTDI_Hi_Speed_USB_To_JTAG_Example.pdf) with correct pinout for using [MPSSE-mode](https://www.ftdichip.com/Support/Documents/AppNotes/AN_135_MPSSE_Basics.pdf). 

# Alternatives
Compared to a UART, the MPSSE-based approach achieves +5x throughput (approaching the FTDI MPSSE hardware limit of 30 MBit/s) and ~3x better latency (reaching the limit set by [USB 2.0 125 us microframe structure](http://www.usbmadesimple.co.uk/ums_6.htm), that is, the interface can reach 8000 independent command-response transactions per second.

# Do I need it and why not
If a conventional UART will do (use FTDI DLL commands beyond 900 kBaud, not e.g. Windows standard serial port), the answer is clearly NO. Performance is bought by complexity and architectural constraints. Most importantly, the RTL implementation must provide readback data in time, where a UART will simply wait.

# What licenses do I need
* Vivado webpack (no cost)
* Visual studio for compilation. A free .NET environment e.g. (sharpDevelop)[https://sourceforge.net/projects/sharpdevelop/] should work but is untested
* FTDI's managed .NET wrapper which is (provided by FTDI)[https://www.ftdichip.com/Support/SoftwareExamples/CodeExamples/CSharp.htm] as a "free download" (included by the terms of its own license)
* (FTDI's D2XX drivers)[https://www.ftdichip.com/Drivers/D2XX.htm] installed on the machine
* The interface does NOT require a Digilent JTAG license, as it is completely independent (but it does not interfere either)

# Bitstream uploader
A .bit file can be uploaded, which e.g. simplifies version management over using flash memory. This feature can be used independently.

# How much space does it need
After stripping off example features (e.g. BRAM), the required resources are minimal, comparable to a UART. The design requires one BSCANE2 instantiation (of which there are four in total - in case of conflicts, change the USER ID both in SW and RTL as needed)

# But it doesn't work!
* Is the FTDI chip already opened e.g. by Vivado?
* Does the board support 30 MBit/s between FTDI chip and FPGA? For example, (this FTDI board)[https://shop.trenz-electronic.de/en/Products/Trenz-Electronic/Open-Hardware/Xmod-FTDI-JTAG-Adapter/] is limited to 15 MBit/s, most likely because of the CPLD
* Does the board require a specific GPO configuration on the FTDI chip e.g. to enable buffers? The example code works on (CMOD A7)[https://store.digilentinc.com/cmod-a7-breadboardable-artix-7-fpga-module/], which requires one GPO-bit to enable JTAG buffers.
* Electrical problems (USB cable, power supply, microcracks in PCB traces are not unheard of.

# But the example design has no clock input!?
The example design runs from the FPGA's on-board ring oscillator (~65 MHz) to make it portable, without knowing the board-specific LOCation of the clock pin. DO use a proper crystal-based clock in any "serious" design.

# How can I debug this with Xilinx tools (Microblaze, ILA)
You can't. The FTDI logical device for JTAG cannot be shared.

If "coexistence" can't be avoided in development, connect another FPGA board through spare GPIOs and instantiate the BSCANE2 on the 2nd board (obviously, not supporting bitstream upload).

# Why does this need to be so absurdly complex?
Because it's as fast as it goes, using only the standard FTDI/JTAG interface (which may be considered "smallest common denominator"). There are a few annoying details that had to be worked around, like splitting off the 8th bit for JTAG state transitions.  

On the bright side: for bitstream upload only, most of the C# code is not needed.

# But what about the rated 480 MBit/s?
Check the parallel mode of the FTDI chip on both parallel devices (MPSSE is, after all, still serial)

# The clock domain crossing gives a warning
There are two clock domains:
* The JTAG port (driven by TCK from the FTDI chip)
* The application clock domain at a "higher" frequency (if in doubt, increase the FTDI clock divider to slow things down on the JTAG side)
The clock domain crossing is unusual in a sense that no synchronizer is used as a design decision (if return data would arrive so late as to cause metastability, it is invalid in any case. The downstream logic is "robust").
It is at the user's discretion to use appropriate constraints, exceptions, or insert a pair of (*ASYNC_REG=TRUE*) FFs. The strategy is simply that the application is required to provide return data in time, and adding a synchronizer at the (slow) JTAG frequency would cut into that timing budget.
