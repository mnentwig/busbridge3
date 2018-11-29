`default_nettype none

// provides byte-level interface to the USERx opcode of the FPGA's JTAG port
// change in o_toggle signals an event
module jtagByteIf(i_dataTx, o_dataRx, o_tx, o_rx, o_sync, o_toggle);
   input wire [7:0] i_dataTx;
   output reg [7:0] o_dataRx 		= 8'dx;
   output reg 	    o_tx 		= 1'b0;
   output reg 	    o_rx 		= 1'b0;
   output reg 	    o_sync	 	= 1'b0;
   output reg 	    o_toggle	 	= 1'b0;
   
   parameter USER = 1; // which USERx port to use
   wire 	    JTAGBSCANE2_CLK;
   wire 	    JTAGBSCANE2_CAPT;
   wire 	    JTAGBSCANE2_SHIFT;
   wire 	    JTAGBSCANE2_TDI;  
   wire 	    JTAGBSCANE2_TDO;   
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
   
   reg [7:0] 	    shifterTx = 8'd0;
   reg [7:0] 	    shifterRx = 8'd0;
   reg [2:0] 	    bitCount = 3'd0;   
   wire [7:0] 	    nextShifterRx = {JTAGBSCANE2_TDI, shifterRx[7:1]};
   wire [7:0] 	    nextShifterTx = {1'b0, shifterTx[7:1]};
   
   always @(posedge JTAGBSCANE2_CLK) begin
      shifterRx 	<= nextShifterRx;
      shifterTx 	<= nextShifterTx; // prelim. assignment
      
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

// connects to jtagByteIf on the JTAG side, provides a simple bus interface on the application side
module busBridge3
  (i_CLK, 	// application-side clock
   i_dataRx,
   i_strobeRx,
   o_dataTx,
   i_strobeTx,
   i_strobeSync,
   
   // application-side bus interface
   o_busAddr,
   o_busData,
   i_busData,
   o_busWe,
   o_busRe,
   i_busAck // signals valid readback data from application
   );
   
   input wire   i_CLK;      
   input wire [7:0] i_dataRx;
   input wire 	    i_strobeRx; // flags update to inbound data, 1..2 cycles AFTER change of i_dataRx
   output wire [7:0] o_dataTx;
   input wire 	     i_strobeTx; // flags sampling of outbound data, 1..2 cycles AFTER sampling instant
   input wire 	     i_strobeSync;
   output reg [31:0] o_busAddr = 32'd0;
   output reg [31:0] o_busData;
   input wire [31:0] i_busData;
   
   output reg 	     o_busWe = 1'b0;   
   output reg 	     o_busRe = 1'b0;
   input wire 	     i_busAck;
   
   localparam STROBE_TX_DELAY = 32'd2; // account for the delay in i_strobeTx in timing measurement
   reg 		     pendingRead = 1'b0;
   reg [15:0] 	     readMargin = 16'hFFFF;
   reg [15:0] 	     readMarginMin = 16'hFFFF;
   
   reg [31:0] 	     shiftIn = 32'd0;
   wire [31:0] 	     nextShiftIn = {i_dataRx, shiftIn[31:8]};

   reg [31:0] 	     shiftOut = 32'd0;
   wire [31:0] 	     nextShiftOut = {8'd0, shiftOut[31:8]};
   assign o_dataTx = shiftOut[7:0];
   
   localparam STATE_IDLE = 8'd0;
   localparam STATE_ADDRINC = 8'd1;
   localparam STATE_WORDWIDTH = 8'd2;
   localparam STATE_NWORDS = 8'd3;
   localparam STATE_ADDRWRITE = 8'd4;
   localparam STATE_WRITE = 8'd5;
   localparam STATE_ADDRREAD = 8'd6;
   localparam STATE_READ = 8'd7;
   localparam STATE_MARGIN = 8'd8;
   reg [1:0] 	     nRemMinusOne = 2'd0;
   reg [7:0] 	     state = STATE_IDLE;
   reg [31:0] 	     addr = 32'dx;
   reg [7:0] 	     config_addrInc = 8'd1;
   reg [1:0] 	     config_wordwidthMinusOne = 2'd3;
   reg [15:0] 	     config_nWordsMinusOne = 16'd0;
   reg [15:0] 	     countNWords = 16'd0;
   
   task doRead(input [31:0] newAddr, input[15:0] paramCountMinusOne);
      begin
	 // === nextShiftIn is written to bus with address ===
	 o_busRe <= 1'b1;
	 o_busAddr <= newAddr;
	 pendingRead <= 1'b1;
	 addr <= newAddr + {24'd0, config_addrInc};
	 readMargin <= 16'd0;	 
	 if (paramCountMinusOne == 16'd0) begin
	    // === done ===
	    state <= STATE_IDLE;
	    nRemMinusOne <= 2'd0;		 
	    countNWords <= 16'dx;		 
	 end else begin
	    // === next word, next address ===
	    countNWords <= paramCountMinusOne - 16'd1;
	    state <= STATE_READ;
	    nRemMinusOne <= config_wordwidthMinusOne;		 
	 end
      end
   endtask   
   always @(posedge i_CLK) begin
      // === preliminary assignments ===
      o_busWe <= 1'b0;
      o_busRe <= 1'b0;      
      o_busAddr <= 32'bx;      
      o_busData <= 32'bx;      
      readMargin <= (readMargin == 16'hFFFF) ? readMargin : readMargin + 16'd1;
      
      if (i_strobeTx) begin
	 shiftOut 	<= nextShiftOut;
	 if (pendingRead)
	   readMarginMin <= 16'd0; // read timed out
	 else
	   readMarginMin <= readMargin < readMarginMin ? readMargin : readMarginMin; // track minimum
      end
      
      if (i_busAck & pendingRead) begin
	 shiftOut <= i_busData;
	 pendingRead <= 1'b0;
	 readMargin <= 16'd0;
      end
      
      // === got new byte? ===
      if (i_strobeRx) begin
	 // === inbound byte into shifter ===
	 shiftIn <= nextShiftIn;
	 nRemMinusOne <= nRemMinusOne - 2'd1;
	 
	 // === shifter full? ===
	 if (nRemMinusOne == 2'd0) begin
	    case (state)
	      STATE_IDLE: begin
		 // === parse command token ===
		 // note: token usually becomes the new state
		 case (i_dataRx)
		   STATE_ADDRWRITE: begin
		      state <= i_dataRx; 
		      nRemMinusOne <= 2'd3;
		   end
		   STATE_WRITE: begin
		      state <= i_dataRx;
		      countNWords <= config_nWordsMinusOne;
		      nRemMinusOne <= config_wordwidthMinusOne;      
		   end
		   STATE_ADDRREAD: begin
		      state <= i_dataRx; 
		      nRemMinusOne <= 2'd3;
		   end
		   STATE_ADDRINC: begin
		      state <= i_dataRx; 
		      nRemMinusOne <= 2'd0;
		   end
		   STATE_WORDWIDTH: begin 
		      state <= i_dataRx; 
		      nRemMinusOne <= 2'd0;
		   end
		   STATE_NWORDS: begin 
		      state <= i_dataRx; 
		      nRemMinusOne <= 2'd1;
		   end
		   STATE_READ: begin
		      doRead(addr, config_nWordsMinusOne);
		   end
		   STATE_MARGIN: begin
		      state <= i_dataRx; 
		      nRemMinusOne <= 2'd0;
		      if (pendingRead) begin
			 shiftOut <= 16'd0;
			 pendingRead <= 1'b0;
		      end else begin
			 shiftOut <= (readMarginMin < STROBE_TX_DELAY) ? 32'd0 : readMarginMin - STROBE_TX_DELAY;
		      end
		      readMarginMin <= 16'hFFFF;
		   end
		   default: begin 
		      state <= STATE_IDLE;
		      nRemMinusOne <= 2'd0;
		      readMargin <= 16'dx;		      
		   end
		 endcase
	      end
	      STATE_ADDRWRITE: begin 
		 addr <= nextShiftIn;
		 state <= STATE_WRITE;
		 countNWords <= config_nWordsMinusOne;
		 nRemMinusOne <= config_wordwidthMinusOne;      
	      end
	      STATE_ADDRREAD: begin
		 doRead(nextShiftIn, config_nWordsMinusOne);
	      end
	      STATE_ADDRINC: begin 
		 config_addrInc <= nextShiftIn[31:24];
		 state <= STATE_IDLE;
		 nRemMinusOne <= 2'd0;
	      end
	      STATE_WORDWIDTH: begin
		 config_wordwidthMinusOne <= nextShiftIn[31:24];
		 state <= STATE_IDLE;
		 nRemMinusOne <= 2'd0;
	      end
	      STATE_NWORDS: begin
		 config_nWordsMinusOne <= nextShiftIn[31:16];
		 state <= STATE_IDLE;
		 nRemMinusOne <= 2'd0;
	      end
	      STATE_WRITE: begin
		 // === nextShiftIn is written to bus with address ===
		 o_busWe <= 1'b1;
		 o_busAddr <= addr;		 
		 case (config_wordwidthMinusOne)
		   2'd0: o_busData <= {24'd0, nextShiftIn[31:24]};		   
		   2'd1: o_busData <= {16'd0, nextShiftIn[31:16]};
		   2'd2: o_busData <= {8'd0, nextShiftIn[31:8]};
		   2'd3: o_busData <= nextShiftIn;
		 endcase
		 addr <= addr + {24'd0, config_addrInc};
		 if (countNWords == 16'd0) begin
		    // === done ===
		    state <= STATE_IDLE;
		    nRemMinusOne <= 2'd0;		 
		    countNWords <= 16'dx;		 
		 end else begin
		    // === next word, next address ===
		    countNWords <= countNWords - 16'd1;
		    nRemMinusOne <= config_wordwidthMinusOne;		 
		 end
	      end
	      STATE_READ: begin
		 doRead(addr, countNWords);
	      end
	      STATE_MARGIN: begin
		 state <= STATE_IDLE;
		 nRemMinusOne <= 2'd0;		 	      
	      end
	      default: begin
		 // impossible
		 state <= STATE_IDLE;
		 nRemMinusOne <= 2'd0;		 	      
	      end
	    endcase
	 end // if nRemMinusOne bytes read
      end // if byte in

      // === reset on new selection of USER1 mode ===
      if (i_strobeSync) begin
	 state <= STATE_IDLE;
	 nRemMinusOne <= 2'd0;
	 pendingRead <= 1'b0;	 
      end
   end   
endmodule

// XDC: outputs single o_strobe pulse on change of i_toggle 
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

// example implementation: basic RAM for testing the interface
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

// top level module, example implementation
module top();   
   wire 	    LED; // we hijack the PROG_DONE LED, which is a fairly common feature
   wire 	    clk; // clock comes from the FPGA's own 65 MHz (-ish) ring oscillator. Please use a proper crystal-based clock in any "serious" design
   STARTUPE2 iStartupE2(.USRCCLKO(1'b0), .USRCCLKTS(1'b0), .USRDONEO(LED), .USRDONETS(1'b0), .CFGMCLK(clk));
   wire 	    CLK = clk;
   
   // === JTAG serial to byte-parallel (JTAG clock domain) ===
   wire [7:0] 	    dataTx;
   wire [7:0] 	    dataRx;
   wire 	    rxStrobeJtagClk;
   wire 	    txStrobeJtagClk;
   wire 	    syncStrobeJtagClk;
   wire 	    evtToggleJtagClk;
   jtagByteIf if1(.i_dataTx(dataTx), .o_dataRx(dataRx), .o_tx(txStrobeJtagClk), .o_rx(rxStrobeJtagClk), .o_sync(syncStrobeJtagClk), .o_toggle(evtToggleJtagClk));
   
   // === XCD to application clock domain ===
   wire 	    evt;
   toggleDet iTd1(.i_clk(CLK), .i_toggle(evtToggleJtagClk), .o_strobe(evt)); // evt changes late
   wire 	    rxStrobe 		= evt & rxStrobeJtagClk;
   wire 	    txStrobe 		= evt & txStrobeJtagClk;
   wire 	    syncStrobe	= evt & syncStrobeJtagClk;
   
   wire [31:0] 	    busAddr;
   wire [31:0] 	    busData;
   wire 	    busWe;
   wire 	    busRe;
   reg [31:0] 	    busData_S2M; // readback data (slave-to-master)
   wire 	    busAck_S2M; // readback data valid (slave-to-master)
   busBridge3 if2
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

   
   // ===================================================================================
   // Below: Example features for testing
   // ===================================================================================
   
   // === demo slave 0 (RAM) ===
   wire [31:0] 	    MEM_outData;
   wire 	    MEM_ack;   
   testmem #(.ADDRVAL(32'hF0000000))iMem
     (.i_clk(CLK), 
      .i_addr(busAddr), 
      .i_we(busWe), 
      .i_data(busData), 
      .i_re(busRe), 
      .o_data(MEM_outData), 
      .o_ack(MEM_ack));
   
   // === simple demo slave 1 (32 bit reg) ===
   reg [31:0] 	    R0 = 32'd0;
   reg 		    R0_ack = 1'b0;
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
   // Please note, a real implementation should be robust towards stray "ACKs" unless it is guaranteed-by-design that a read can never time out
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
