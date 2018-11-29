`default_nettype none
`include "busBridge2.v"

module testmem(i_clk, i_addr, o_ack, i_we, i_data, i_re, o_data);
   parameter ADDRVAL = 32'h00000000;
   localparam ADDRMASK = ~32'h00003FFF;
   input wire i_clk;
   input wire [31:0] i_addr;   
   output reg 	     o_ack = 1'b0;
   input wire 	     i_we;
   input wire [31:0] i_data;   
   input wire 	     i_re;
   output reg [31:0] o_data = 32'd0;
   
   reg [31:0] 	     memmodel[16383:0];
   reg [31:0] 	     data3 = 32'dx;
   reg [31:0] 	     data2 = 32'dx;
   reg [31:0] 	     data1 = 32'dx;
   reg 		     ack3 = 1'b0;
   reg 		     ack2 = 1'b0;
   reg 		     ack1 = 1'b0;

   reg 		     we2 = 1'b0;
   reg 		     re2 = 1'b0;
   reg [31:0] 	     addr2 = 32'dx;
   reg [31:0] 	     inData2 = 32'dx;

   wire 	     ce2 = (addr2 & ADDRMASK) == ADDRVAL;
   always @(posedge i_clk) begin
      // === register input ===
      we2 <= i_we;
      re2 <= i_re;
      addr2 <= i_addr;
      inData2 <= i_data;

      // === memory operations ===
      if (we2 & ce2)
	memmodel[addr2] <= inData2;
      if (re2 & ce2)
	data2 <= memmodel[addr2];
      else
	data2 <= 32'dx;

      // === acknowledge ===
      // TBD omit WE in this application
      //ack2 <= (we2 | re2) & ce2;
      ack2 <= re2 & ce2;

      // === BRAM output registers ===
      data1 <= data2;      
      ack1 <= ack2;      
      
      // === outputs ===
      o_data  <= data1;
      o_ack  <= ack1;
   end         
endmodule

  module jtagByteIf(i_dataTx, o_dataRx, o_tx, o_rx, o_sync, o_toggle);
   input wire [7:0] i_dataTx;
   output reg [7:0] o_dataRx 		= 8'dx;
   output reg 	    o_tx 		= 1'b0;
   output reg 	    o_rx 		= 1'b0;
   output reg 	    o_sync	 	= 1'b0;
   output reg 	    o_toggle	 	= 1'b0;
   
   parameter USER = 1;   
   wire 		      JTAGBSCANE2_CLK;
   wire 		      JTAGBSCANE2_CAPT;
   wire 		      JTAGBSCANE2_SHIFT;
   wire 		      JTAGBSCANE2_TDI;  
   wire 		      JTAGBSCANE2_TDO;   
   BSCANE2 #(.JTAG_CHAIN(USER)) iBSCANE2
     (.DRCK(    JTAGBSCANE2_CLK),
      .CAPTURE( JTAGBSCANE2_CAPT),
      .SHIFT(   JTAGBSCANE2_SHIFT),
      .TDI(     JTAGBSCANE2_TDI),
      .TDO(	JTAGBSCANE2_TDO),
      .UPDATE(  /*pio5*/),
      .RESET(   /*pio6*/),
      .RUNTEST( /*pio7*/),
      .SEL(     /*pio8*/),
      .TCK(     /*pio9*/),
      .TMS());
   
   reg [7:0] 		      shifterTx = 8'd0;
   reg [7:0] 		      shifterRx = 8'd0;
   reg [2:0] 		      bitCount = 3'd0;   
   wire [7:0] 		      nextShifterRx = {JTAGBSCANE2_TDI, shifterRx[7:1]};
   wire [7:0] 		      nextShifterTx = {1'b0, shifterTx[7:1]};
   
   always @(posedge JTAGBSCANE2_CLK) begin
      shifterRx 	<= nextShifterRx;
      shifterTx 	<= nextShifterTx;
      
      if (JTAGBSCANE2_CAPT) begin
	 bitCount 	<= 3'd0;
	 shifterTx 	<= i_dataTx;
	 // === XCD: set all outputs ===
	 o_tx	 	<= 1'b1;
	 o_rx		<= 1'b0;
	 o_sync		<= 1'b1;
	 // === XCD: raise event ===
	 o_toggle	<= ~o_toggle;	 
      end else if (JTAGBSCANE2_SHIFT) begin
	 bitCount <= bitCount + 3'd1;	 
	 if (bitCount == 3'd7) begin
	    shifterTx 	<= i_dataTx;
	    o_dataRx 	<= nextShifterRx;
	    // === XCD: set all outputs ===
	    o_tx 	<= 1'b1;
	    o_rx	<= 1'b1;
	    o_sync 	<= 1'b0;
	    // === XCD: raise event ===
	    o_toggle	<= ~o_toggle;	 
	 end
      end
   end   
   assign JTAGBSCANE2_TDO = shifterTx[0];
endmodule

module toggleDet(i_clk, i_toggle, o_strobe);
   input wire i_clk;
   input wire i_toggle;
   output wire o_strobe;
   
   (* ASYNC_REG = "TRUE" *)reg 	      t1 = 1'b0;
   (* ASYNC_REG = "TRUE" *)reg 	      t2 = 1'b0;
   reg 	       t3 = 1'b0;
   always @(posedge i_clk) begin
      t1 <= i_toggle;
      t2 <= t1;
      t3 <= t2;
   end
   assign o_strobe = t2 ^ t3;
endmodule

module top();
   wire       evt;
   
   wire 	    LED;   
   wire 	    clk;   
   STARTUPE2 iStartupE2(.USRCCLKO(1'b0), .USRCCLKTS(1'b0), .USRDONEO(LED), .USRDONETS(1'b0), .CFGMCLK(clk));
   wire 	    CLK = clk;
   
   wire [7:0] dataTx;
   wire [7:0] dataRx;
   wire       rxStrobeJtagClk;
   wire       txStrobeJtagClk;
   wire       syncStrobeJtagClk;
   wire       evtToggleJtagClk;
   jtagByteIf if1(.i_dataTx(dataTx), .o_dataRx(dataRx), .o_tx(txStrobeJtagClk), .o_rx(rxStrobeJtagClk), .o_sync(syncStrobeJtagClk), .o_toggle(evtToggleJtagClk));
   
   // === XCD ===
   toggleDet iTd1(.i_clk(CLK), .i_toggle(evtToggleJtagClk), .o_strobe(evt)); // evt changes late
   wire       rxStrobe 		= evt & rxStrobeJtagClk;
   wire       txStrobe 		= evt & txStrobeJtagClk;
   wire       syncStrobe	= evt & syncStrobeJtagClk;
   
   wire [31:0] busAddr;
   wire [31:0] busData;
   wire        busWe;
   wire        busRe;
   reg [31:0]  busData_S2M;   
   wire        busAck_S2M;   
   
   busBridge2 if2
     (.i_CLK(CLK),
      .i_dataRx(dataRx),
      .i_strobeRx(rxStrobe),
      .o_dataTx(dataTx),
      .i_strobeTx(txStrobe),
      .i_strobeSync(syncStrobe),
      .o_busAddr(busAddr),
      .o_busData(busData),
      .o_busWe(busWe),
      .o_busRe(busRe),
      .i_busData(busData_S2M),
      .i_busAck(busAck_S2M));
   
   // === demo slave 0 (RAM) ===
   wire [31:0] MEM_outData;
   wire        MEM_ack;   
   testmem #(.ADDRVAL(32'hF0000000))iMem
     (.i_clk(CLK), 
      .i_addr(busAddr), 
      .i_we(busWe), 
      .i_data(busData), 
      .i_re(busRe), 
      .o_data(MEM_outData), 
      .o_ack(MEM_ack));
   
   // === simple demo slave 1 (32 bit reg) ===
   reg [31:0] 	       R0 = 32'd0;
   reg 		       R0_ack = 1'b0;
   always @(posedge CLK) begin
      R0_ack <= 1'b0;      
      if (busAddr == 32'h12345678) begin
	 if (busWe) R0 <= busData;
	 if (busRe) R0_ack <= 1'b1;
      end
   end
   assign LED = R0[0];   
      
   // === simple demo slave 2 (32 bit reg) ===
   reg [31:0] 	       R1 = 32'd0;
   reg 		       R1_ack = 1'b0;
   always @(posedge CLK) begin
      R1_ack <= 1'b0;      
      if (busAddr == 32'h87654321) begin
	 if (busWe) R1 <= busData;
	 if (busRe) R1_ack <= 1'b1;
      end
   end

   // === demo slave 3 (32 bit reg) ===
   // write sets number of read clock cycles (0:force timeout)
   // read delays for the programmed number of cycles
   // read returns the programmed number of cycles
   reg [31:0] 	       R2a = 32'd0;
   reg [31:0] 	       R2b = 32'd0;
   reg 		       R2_ack = 1'b0;
   always @(posedge CLK) begin
      R2b 	<= R2b == 0 ? R2b : R2b-32'd1;
      R2_ack 	<= R2b == 32'd1 ? 1'b1 : 1'b0;
      
      if (busAddr == 32'h98765432) begin
	 if (busWe) R2a <= busData;
	 if (busRe) R2b <= R2a;
      end
   end
   
   // === bus return mux ===
   wire [31:0] 	ackSel = {28'd0, R2_ack, R1_ack, R0_ack, MEM_ack};   
   always @(*)
   case (ackSel)
     32'h008 : busData_S2M = R2a;
     32'h004 : busData_S2M = R1;     
     32'h002 : busData_S2M = R0;     
     32'h001 : busData_S2M = MEM_outData;
     default: busData_S2M = 32'dx;
   endcase
   assign busAck_S2M = |ackSel;   
endmodule
