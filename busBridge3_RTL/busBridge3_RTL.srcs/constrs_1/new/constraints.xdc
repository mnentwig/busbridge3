set_property BITSTREAM.GENERAL.COMPRESS TRUE [current_design]

# Clock signal 12 MHz
#set_property -dict {PACKAGE_PIN L17 IOSTANDARD LVCMOS33} [get_ports CLK12]
#create_clock -period 83.330 -name sys_clk_pin -waveform {0.000 41.660} -add [get_ports CLK12]

set_property CFGBVS VCCO [current_design]
set_property CONFIG_VOLTAGE 3.3 [current_design]

# application-domain clock from ring oscillator ~65 MHz (15 ns => 66.667 MHz)
create_clock -period 15 -name RINGOSC_CLK -waveform {0.000 7.5} [get_pins iStartupE2/CFGMCLK]

# JTAG runs at 30 MHz max (FT2232H clk divider 0). This clock is provided by the different BSCANE2 primitives
create_clock -period 33.330 -name USER_CLK -waveform {0.000 16.660} [list [get_pins iUser2demo/ifUserDemo/iBSCANE2/DRCK] [get_pins if1/iBSCANE2/DRCK]]

# clock domain crossings from JTAG to application and back
# demand that the spread between crossing signals remains within one period of the application clock (approx.)
# then sample one application clock cycle after the crossing
# Re-check this for a specific application
# assuming
# - 8 ns => 125 MHz max in application (critical for correct detection of parallel events on CDX)
# - 30 ns => 32 MHz max in JTAG (largely academic, because the design should provide readback data much earlier, to be validated with built-in margin detection)
set_min_delay 0 -from [get_clocks USER_CLK] -to [get_clocks RINGOSC_CLK]
set_min_delay 0 -from [get_clocks RINGOSC_CLK] -to [get_clocks USER_CLK]
set_max_delay 8 -from [get_clocks USER_CLK] -to [get_clocks RINGOSC_CLK]
set_max_delay 30 -from [get_clocks RINGOSC_CLK] -to [get_clocks USER_CLK]

# quick-and-dirty variant
# set_false_path -from [get_clocks USER1_CLK] -to [get_clocks RINGOSC_CLK]
# set_false_path -from [get_clocks RINGOSC_CLK] -to [get_clocks USER1_CLK]
# set_false_path -from [get_clocks USER2_CLK] -to [get_clocks RINGOSC_CLK]
# set_false_path -from [get_clocks RINGOSC_CLK] -to [get_clocks USER2_CLK]
# create_waiver -type METHODOLOGY -id {TIMING-9} -user "xc6lx45" -desc "data is sampled 1 cycle after change" -timestamp "Sun Nov 11 21:44:37 GMT 2018"
