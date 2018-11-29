set_property BITSTREAM.GENERAL.COMPRESS TRUE [current_design]

# Clock signal 12 MHz
#set_property -dict {PACKAGE_PIN L17 IOSTANDARD LVCMOS33} [get_ports CLK12]
#create_clock -period 83.330 -name sys_clk_pin -waveform {0.000 41.660} -add [get_ports CLK12]
#create_clock -add -name sys_clk_pin [get_pins if1/iBSCANE2/DRCK]

set_property CFGBVS VCCO [current_design]
set_property CONFIG_VOLTAGE 3.3 [current_design]

# JTAG runs at 30 MHz max (FT2232H clk divider 0)
create_clock -period 33.330 -name JTAGBSCANE2_CLK -waveform {0.000 16.660} [get_pins if1/iBSCANE2/DRCK]
create_clock -period 16 -name RINGOSC_CLK -waveform {0.000 8} [get_pins iStartupE2/CFGMCLK]
#create_generated_clock -name clkReplica -source [get_ports CLK12] -divide_by 1 [get_pins if1/iBSCANE2/DRCK]

set_false_path -from [get_clocks JTAGBSCANE2_CLK] -to [get_clocks RINGOSC_CLK]
set_false_path -from [get_clocks RINGOSC_CLK] -to [get_clocks JTAGBSCANE2_CLK]

create_waiver -type METHODOLOGY -id {TIMING-9} -user "gk" -desc "data is sampled 1 cycle late" -timestamp "Sun Nov 11 21:44:37 GMT 2018"
