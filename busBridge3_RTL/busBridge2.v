module busBridge2
  (i_CLK, 
   i_dataRx,
   i_strobeRx,
   o_dataTx,
   i_strobeTx,
   i_strobeSync,
   o_busAddr,
   o_busData,
   i_busData,
   o_busWe,
   o_busRe,
   i_busAck);
   
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
