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

# Do I need it
If a conventional UART will do (use FTDI DLL commands beyond 900 kBaud, not e.g. Windows standard serial port), the answer is clearly 

# Do I need additional licenses
No, the interface should work regardless of the FTDI EEPROM contents (e.g. the license key on a Digilent board is not required but does not interfere either)

# Bitstream uploader
A .bit file can be uploaded, which e.g. simplifies version management over using flash memory. This feature can be used independently.

# But it doesn't work!
* Is the FTDI chip already opened e.g. by Vivado?
* Does the board support 30 MBit/s between FTDI chip and FPGA? For example, (this FTDI board)[https://shop.trenz-electronic.de/en/Products/Trenz-Electronic/Open-Hardware/Xmod-FTDI-JTAG-Adapter/] is limited to 15 MBit/s, most likely because of the CPLD
* Does the board require a specific GPO configuration on the FTDI chip e.g. to enable buffers? The example code works on (CMOD A7)[https://store.digilentinc.com/cmod-a7-breadboardable-artix-7-fpga-module/], which requires one GPO-bit to enable JTAG buffers.
