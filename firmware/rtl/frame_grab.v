// Copy a frozen display slot to a disjoint snapshot region. Requests stay
// valid with stable address/data until the arbiter grants them via I_ready.
`timescale 1ns / 1ps
module frame_grab #(
    parameter [24:0] SRC_BASE=25'd0,
    parameter [24:0] DST_BASE=25'd25_000_000,
    parameter integer WORDS=172800, AW=25
)(
    input wire I_clk,I_rst_n,I_start,
    input wire [AW-1:0] I_src_base,
    output wire O_busy,
    output reg O_done,
    output wire O_rd_en,O_wr_en,
    output wire [AW-1:0] O_addr,
    output wire [127:0] O_wr_data,
    input wire I_ready,I_rd_valid,
    input wire [127:0] I_rd_data
);
    localparam IDLE=0,READ=1,WAIT_DATA=2,WRITE=3;
    reg [1:0] state;
    reg [31:0] index;
    reg [AW-1:0] source;
    reg [127:0] data;
    assign O_busy = state != IDLE;
    assign O_rd_en = state == READ;
    assign O_wr_en = state == WRITE;
    assign O_addr = (state == WRITE ? DST_BASE[AW-1:0] : source) + (index << 3);
    assign O_wr_data = data;
    always @(posedge I_clk or negedge I_rst_n) begin
        if (!I_rst_n) begin
            state<=IDLE;index<=0;source<=SRC_BASE;data<=0;O_done<=0;
        end else begin
            O_done<=0;
            case(state)
                IDLE: if(I_start) begin
                    source<=I_src_base;index<=0;
                    if (WORDS == 0) O_done<=1; else state<=READ;
                end
                READ: if(I_ready) state<=WAIT_DATA;
                WAIT_DATA: if(I_rd_valid) begin data<=I_rd_data;state<=WRITE;end
                WRITE: if(I_ready) begin
                    if(index == WORDS-1) begin state<=IDLE;O_done<=1;end
                    else begin index<=index+1'b1;state<=READ;end
                end
            endcase
        end
    end
endmodule
