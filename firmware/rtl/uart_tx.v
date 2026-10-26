// ============================================================================
//  uart_tx.v -- simple 8N1 UART transmitter.
//
//  Parameterised by clock frequency and baud rate so the SAME code can later be
//  re-synthesised at 921600 or 3 Mbaud by changing one parameter.
//
//  Derived from the vendor example 16_uart_rs232/uart_tx.v (same CYCLE formula,
//  same 4-state structure) but re-expressed with a single counter and no
//  reliance on undeclared nets.
//
//  H1 is the board's UART TX pin (manual 6.4, confirmed against the vendor
//  example's uart_test.adc).
// ============================================================================
`timescale 1ns / 1ps

module uart_tx #(
    parameter integer CLK_FRE   = 75_000_000,   // Hz
    parameter integer BAUD_RATE = 115_200
)(
    input  wire       I_clk,
    input  wire       I_rst_n,
    input  wire [7:0] I_data,
    input  wire       I_valid,      // pulse: latch I_data and start sending
    output reg        O_txd,
    output reg        O_busy
);
    // Bit period in clock cycles, integer division as in the vendor example.
    //
    // CLK_FRE MUST be the real clock rate or the baud rate comes out wrong.
    // Historical notes called this DDR clock 132.820 MHz without an independent
    // measurement. On 2026-10-15, complete GDB1 packets with the old 1152-cycle
    // divider decoded at 57600, consistent with route usr_clk = 66.667 MHz.
    // The top now uses that nominal rate. Verify complete bytes and CRC on the
    // board; a recognizable header alone does not establish the UART rate.
    localparam integer CYCLE = CLK_FRE / BAUD_RATE;

    localparam ST_IDLE  = 2'd0,
               ST_START = 2'd1,
               ST_DATA  = 2'd2,
               ST_STOP  = 2'd3;

    reg [1:0]  state_r, state_w;
    reg [15:0] cnt_r,   cnt_w;
    reg [2:0]  bit_r,   bit_w;
    reg [7:0]  data_r,  data_w;
    reg        txd_r,   txd_w;
    reg        busy_r,  busy_w;

    always @(*) begin
        state_w = state_r;
        cnt_w   = cnt_r;
        bit_w   = bit_r;
        data_w  = data_r;
        txd_w   = txd_r;
        busy_w  = busy_r;

        case (state_r)
            ST_IDLE: begin
                txd_w  = 1'b1;                 // idle line is high
                busy_w = 1'b0;
                if (I_valid) begin
                    data_w  = I_data;
                    busy_w  = 1'b1;
                    cnt_w   = 16'd0;
                    bit_w   = 3'd0;
                    state_w = ST_START;
                end
            end
            ST_START: begin
                txd_w = 1'b0;                  // start bit
                if (cnt_r == CYCLE - 1) begin
                    cnt_w   = 16'd0;
                    state_w = ST_DATA;
                end
                else
                    cnt_w = cnt_r + 16'd1;
            end
            ST_DATA: begin
                txd_w = data_r[bit_r];         // LSB first
                if (cnt_r == CYCLE - 1) begin
                    cnt_w = 16'd0;
                    if (bit_r == 3'd7) begin
                        bit_w   = 3'd0;
                        state_w = ST_STOP;
                    end
                    else
                        bit_w = bit_r + 3'd1;
                end
                else
                    cnt_w = cnt_r + 16'd1;
            end
            ST_STOP: begin
                txd_w = 1'b1;                  // stop bit
                if (cnt_r == CYCLE - 1) begin
                    cnt_w   = 16'd0;
                    busy_w  = 1'b0;
                    state_w = ST_IDLE;
                end
                else
                    cnt_w = cnt_r + 16'd1;
            end
            default: state_w = ST_IDLE;
        endcase
    end

    always @(posedge I_clk or negedge I_rst_n) begin
        if (!I_rst_n) begin
            state_r <= ST_IDLE;
            cnt_r   <= 16'd0;
            bit_r   <= 3'd0;
            data_r  <= 8'd0;
            txd_r   <= 1'b1;
            busy_r  <= 1'b0;
        end
        else begin
            state_r <= state_w;
            cnt_r   <= cnt_w;
            bit_r   <= bit_w;
            data_r  <= data_w;
            txd_r   <= txd_w;
            busy_r  <= busy_w;
        end
    end

    always @(*) O_txd = txd_r;
    always @(*) O_busy = busy_r;

endmodule
