`timescale 1ns/1ps
// Single-owner dual-clock frame buffer: CSI owns it while capturing;
// HDMI/UART owns it until the final CRC byte has completely drained.
module usb_preview #(
    parameter integer SRC_W=1280, SRC_H=720,
    parameter integer TX_CLK_HZ=75000000, BAUD=3000000
)(
    input wire I_rst_n,
    input wire I_capture_clk, I_capture_enable,
    input wire I_sof, I_valid, I_last,
    input wire [95:0] I_rgb,
    input wire I_tx_clk, I_tx_enable, I_allow_start,
    output wire O_txd, O_busy
);
    localparam integer W=SRC_W/8, H=SRC_H/8, PIXELS=W*H;
    localparam [15:0] W16=W, H16=H;
    reg [15:0] frame_ram [0:16383];
    reg [8:0] col;
    reg [9:0] row;
    reg [13:0] write_index;
    reg capture, ready_toggle;
    reg ack_toggle;
    (* ASYNC_REG="TRUE" *) reg [1:0] ack_sync,ready_sync;
    wire sample=I_valid && !I_sof && capture && col[0] && row[2:0]==3'd4 && row<SRC_H && col<SRC_W/4;
    wire [15:0] rgb565={I_rgb[95:91],I_rgb[87:82],I_rgb[79:75]};
    // No reset on memory ports: infer true dual-clock block RAM.
    always @(posedge I_capture_clk) if(sample) frame_ram[write_index]<=rgb565;
    always @(posedge I_capture_clk or negedge I_rst_n) begin
        if(!I_rst_n) begin col<=0;row<=0;write_index<=0;capture<=0;ready_toggle<=0;ack_sync<=0;end
        else begin
            ack_sync<={ack_sync[0],ack_toggle};
            if(!I_capture_enable) capture<=0;
            if(I_sof) begin
                col<=I_valid&&!I_last ? 9'd1:9'd0;
                row<=I_valid&&I_last ? 10'd1:10'd0;
                write_index<=0;
                capture<=I_capture_enable && ready_toggle==ack_sync[1];
            end else if(I_valid) begin
                if(I_last)begin col<=0;row<=row+1'b1;end else col<=col+1'b1;
            end
            if(sample) begin
                if(write_index==PIXELS-1)begin ready_toggle<=~ready_toggle;capture<=0;end
                else write_index<=write_index+1'b1;
            end
        end
    end
    reg [13:0] read_address;
    reg [15:0] read_word, pixel;
    always @(posedge I_tx_clk) read_word<=frame_ram[read_address];
    function [15:0] crc_byte;
        input [15:0] c;input [7:0] b;reg [15:0] v;integer j;
        begin v=c^{b,8'd0};for(j=0;j<8;j=j+1)v=v[15]?(v<<1)^16'h1021:v<<1;crc_byte=v;end
    endfunction
    function [7:0] header;
        input [3:0] i;
        begin case(i)
        0:header="H";1:header="X";2:header="P";3:header="1";
        4:header=W16[7:0];5:header=W16[15:8];6:header=H16[7:0];7:header=H16[15:8];
        8:header=8'd2;default:header=0;endcase end
    endfunction
    localparam IDLE=0,HDR=1,REQ=2,WAIT1=3,WAIT2=4,LO=5,HI=6,CRCLO=7,CRCHI=8,DRAIN=9;
    reg [3:0] state,header_index;
    reg [13:0] pixel_index;
    reg [15:0] crc;
    reg [7:0] tx_data;
    reg tx_start;
    wire tx_busy;
    assign O_busy=(state!=IDLE);
    uart_tx #(.CLK_FRE(TX_CLK_HZ),.BAUD_RATE(BAUD)) u_tx(
        .I_clk(I_tx_clk),.I_rst_n(I_rst_n),.I_data(tx_data),.I_valid(tx_start),.O_txd(O_txd),.O_busy(tx_busy));
    always @(posedge I_tx_clk or negedge I_rst_n) begin
        if(!I_rst_n) begin
            ready_sync<=0;ack_toggle<=0;state<=IDLE;header_index<=0;pixel_index<=0;
            read_address<=0;pixel<=0;crc<=16'hffff;tx_data<=0;tx_start<=0;
        end else begin
            ready_sync<={ready_sync[0],ready_toggle};tx_start<=0;
            case(state)
            IDLE: begin
                if(!I_tx_enable) ack_toggle<=ready_sync[1];
                else if(I_allow_start && ready_sync[1]!=ack_toggle) begin
                    state<=HDR;header_index<=0;pixel_index<=0;crc<=16'hffff;
                end
            end
            HDR: if(!tx_busy&&!tx_start)begin
                tx_data<=header(header_index);tx_start<=1;
                crc<=crc_byte(crc,header(header_index));
                if(header_index==9)state<=REQ;else header_index<=header_index+1'b1;
            end
            REQ:begin read_address<=pixel_index;state<=WAIT1;end
            WAIT1:state<=WAIT2;
            WAIT2:begin pixel<=read_word;state<=LO;end
            LO:if(!tx_busy&&!tx_start)begin tx_data<=pixel[7:0];tx_start<=1;crc<=crc_byte(crc,pixel[7:0]);state<=HI;end
            HI:if(!tx_busy&&!tx_start)begin
                tx_data<=pixel[15:8];tx_start<=1;crc<=crc_byte(crc,pixel[15:8]);
                if(pixel_index==PIXELS-1)state<=CRCLO;else begin pixel_index<=pixel_index+1'b1;state<=REQ;end
            end
            CRCLO:if(!tx_busy&&!tx_start)begin tx_data<=crc[7:0];tx_start<=1;state<=CRCHI;end
            CRCHI:if(!tx_busy&&!tx_start)begin tx_data<=crc[15:8];tx_start<=1;state<=DRAIN;end
            DRAIN:if(!tx_busy&&!tx_start)begin ack_toggle<=ready_sync[1];state<=IDLE;end
            default:state<=IDLE;
            endcase
        end
    end
endmodule
