//============================================================================
//  spot_uart.v -- ASCII telemetry for the APT spot tracker
//============================================================================
//  Emits one line per published frame at 115200 8N1:
//
//      XC:0632.41 YC:0359.83 DX:-008.20 DY:+000.10 N:001234
//
//  54 bytes = 4.7 ms at 115200, well inside the 16.7 ms of a 60 Hz frame.
//  The top level multiplexes this transmitter with the frame-grab transmitter
//  on the same pin, so a capture in progress always wins.
//============================================================================
`timescale 1ns/1ps

module spot_uart #(
    parameter integer CLK_FRE = 75_000_000,
    parameter integer EXT_STATUS = 0,
    parameter integer BAUD    = 115_200
)(
    input  wire        I_clk,
    input  wire        I_rst_n,
    input  wire        I_frame,        // one-cycle pulse: a new fix is ready
    input  wire [19:0] I_xc,
    input  wire [19:0] I_yc,
    input  wire signed [19:0] I_dx,
    input  wire signed [19:0] I_dy,
    input  wire [1:0] I_mode,
    input wire I_agc,
    input wire [7:0] I_gain_ex,
    input  wire [19:0] I_n,
    output wire        O_busy,
    output wire        O_txd
);

    localparam integer LEN = EXT_STATUS ? 73 : 54;
    reg [1:0] meta_mode;reg meta_agc;reg [8:0] meta_gain;
    wire [15:0] meta_product=I_gain_ex*16'd100;

    // ------------------------------------------------------------ BCD helpers
    function [3:0] th;  input [11:0] v; begin th = (v / 12'd1000) % 4'd10; end endfunction
    function [3:0] hu;  input [11:0] v; begin hu = (v / 12'd100)  % 4'd10; end endfunction
    function [3:0] te;  input [11:0] v; begin te = (v / 12'd10)   % 4'd10; end endfunction
    function [3:0] on;  input [11:0] v; begin on = v % 4'd10;              end endfunction

    function [3:0] n5; input [19:0] v; begin n5 = (v / 20'd100000) % 4'd10; end endfunction
    function [3:0] n4;  input [19:0] v; begin n4 = (v / 20'd10000) % 4'd10; end endfunction
    function [3:0] n3;  input [19:0] v; begin n3 = (v / 20'd1000)  % 4'd10; end endfunction
    function [3:0] n2;  input [19:0] v; begin n2 = (v / 20'd100)   % 4'd10; end endfunction
    function [3:0] n1;  input [19:0] v; begin n1 = (v / 20'd10)    % 4'd10; end endfunction
    function [3:0] n0;  input [19:0] v; begin n0 = v % 20'd10;              end endfunction

    function [7:0] asc; input [3:0] d; begin asc = 8'h30 + d; end endfunction  // 8 bits: ASCII, not a 4-bit digit

    // ------------------------------------------------------------ latched fix
    reg [11:0] xi, yi, adxi, adyi;
    reg [7:0]  xf, yf, dxf, dyf;      // two decimal fraction digits, 0..99
    reg        dxn, dyn;
    reg [19:0] nn;

    wire signed [19:0] adx = (I_dx < 0) ? -I_dx : I_dx;
    wire signed [19:0] ady = (I_dy < 0) ? -I_dy : I_dy;

    always @(posedge I_clk or negedge I_rst_n) begin
        if (!I_rst_n) begin
            xi <= 12'd0; yi <= 12'd0; adxi <= 12'd0; adyi <= 12'd0;
            xf <= 8'd0; yf <= 8'd0; dxf <= 8'd0; dyf <= 8'd0;
            dxn <= 1'b0; dyn <= 1'b0; nn <= 20'd0;meta_mode<=0;meta_agc<=0;meta_gain<=100;
        end
        else if (I_frame) begin
            xi   <= I_xc[19:8];
            yi   <= I_yc[19:8];
            adxi <= adx[19:8];
            adyi <= ady[19:8];
            xf   <= (I_xc[7:0] * 16'd100) >> 8;
            yf   <= (I_yc[7:0] * 16'd100) >> 8;
            dxf  <= (adx[7:0]  * 16'd100) >> 8;
            dyf  <= (ady[7:0]  * 16'd100) >> 8;
            dxn  <= (I_dx < 0);
            dyn  <= (I_dy < 0);
            nn   <= I_n;meta_mode<=I_mode;meta_agc<=I_agc;meta_gain<=9'd100+{1'b0,meta_product[14:7]};
        end
    end

    // ------------------------------------------------------------ byte table
    reg [6:0] idx;
    reg       active;
    reg [7:0] byte_sel;
    reg       byte_vld;
    wire      tx_busy;
    reg [7:0] tx_data;
    reg       tx_start;
    assign O_busy = active || tx_busy || tx_start;

    function [7:0] byte_at;           // contents of the line, by index
        input [6:0] k;
        begin
            case (k)
            7'd0:  byte_at = "X";
            7'd1:  byte_at = "C";
            7'd2:  byte_at = ":";
            7'd3:  byte_at = asc(th(xi));
            7'd4:  byte_at = asc(hu(xi));
            7'd5:  byte_at = asc(te(xi));
            7'd6:  byte_at = asc(on(xi));
            7'd7:  byte_at = ".";
            7'd8:  byte_at = asc(te({4'd0, xf}));
            7'd9:  byte_at = asc(on({4'd0, xf}));
            7'd10: byte_at = " ";
            7'd11: byte_at = "Y";
            7'd12: byte_at = "C";
            7'd13: byte_at = ":";
            7'd14: byte_at = asc(th(yi));
            7'd15: byte_at = asc(hu(yi));
            7'd16: byte_at = asc(te(yi));
            7'd17: byte_at = asc(on(yi));
            7'd18: byte_at = ".";
            7'd19: byte_at = asc(te({4'd0, yf}));
            7'd20: byte_at = asc(on({4'd0, yf}));
            7'd21: byte_at = " ";
            7'd22: byte_at = "D";
            7'd23: byte_at = "X";
            7'd24: byte_at = ":";
            7'd25: byte_at = dxn ? "-" : "+";
            7'd26: byte_at = asc(hu(adxi));
            7'd27: byte_at = asc(te(adxi));
            7'd28: byte_at = asc(on(adxi));
            7'd29: byte_at = ".";
            7'd30: byte_at = asc(te({4'd0, dxf}));
            7'd31: byte_at = asc(on({4'd0, dxf}));
            7'd32: byte_at = " ";
            7'd33: byte_at = "D";
            7'd34: byte_at = "Y";
            7'd35: byte_at = ":";
            7'd36: byte_at = dyn ? "-" : "+";
            7'd37: byte_at = asc(hu(adyi));
            7'd38: byte_at = asc(te(adyi));
            7'd39: byte_at = asc(on(adyi));
            7'd40: byte_at = ".";
            7'd41: byte_at = asc(te({4'd0, dyf}));
            7'd42: byte_at = asc(on({4'd0, dyf}));
            7'd43: byte_at = " ";
            7'd44: byte_at = "N";
            7'd45: byte_at = ":";
            7'd46: byte_at = asc(n5(nn));
            7'd47: byte_at = asc(n4(nn));
            7'd48: byte_at = asc(n3(nn));
            7'd49: byte_at = asc(n2(nn));
            7'd50: byte_at = asc(n1(nn));
            7'd51: byte_at = asc(n0(nn));
            7'd52: byte_at = EXT_STATUS ? 8'h20 : 8'h0d;
            7'd53: byte_at = EXT_STATUS ? "M" : 8'h0a;
            7'd54: byte_at=":";7'd55: byte_at=asc({2'd0,meta_mode});
            7'd56: byte_at=" ";7'd57: byte_at="A";7'd58: byte_at=":";7'd59: byte_at=meta_agc?"1":"0";
            7'd60: byte_at=" ";7'd61: byte_at="G";7'd62: byte_at=":";
            7'd63: byte_at=asc(hu({3'd0,meta_gain}));7'd64: byte_at=asc(te({3'd0,meta_gain}));7'd65: byte_at=asc(on({3'd0,meta_gain}));
            7'd66: byte_at=" ";7'd67: byte_at="V";7'd68: byte_at=":";7'd69: byte_at="1";7'd70: byte_at="8";
            7'd71: byte_at=8'h0d;7'd72: byte_at=8'h0a;
            default: byte_at = 8'h20;
            endcase
        end
    endfunction

    always @(posedge I_clk or negedge I_rst_n) begin
        if (!I_rst_n) begin
            idx       <= 7'd0;
            active    <= 1'b0;
            tx_data   <= 8'd0;
            tx_start  <= 1'b0;
        end
        else begin
            tx_start <= 1'b0;
            if (!active) begin
                if (I_frame) begin
                    idx    <= 7'd0;
                    active <= 1'b1;
                end
            end
            else if (!tx_busy && !tx_start) begin
                tx_data  <= byte_at(idx);
                tx_start <= 1'b1;
                if (idx == LEN - 1) begin
                    active <= 1'b0;
                end
                else begin
                    idx <= idx + 7'd1;
                end
            end
        end
    end

    uart_tx #(
        .CLK_FRE   (CLK_FRE),
        .BAUD_RATE (BAUD)
    ) u_tx (
        .I_clk   (I_clk),
        .I_rst_n (I_rst_n),
        .I_data  (tx_data),
        .I_valid (tx_start),
        .O_txd   (O_txd),
        .O_busy  (tx_busy)
    );

endmodule
