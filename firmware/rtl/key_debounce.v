//=============================================================================
//  key_debounce.v -- mechanical key debouncer, 20 ms by default.
//
//  WHY NOT the vendor's key_remove_shakes.v
//      That module counts with a 17-bit counter.  On this board the keys are
//      sampled in the HDMI pixel clock domain at 75 MHz, where 2^17 counts is
//      only 1.75 ms -- an order of magnitude short of a real 20 ms debounce,
//      and shorter than the bounce of a typical metal-dome switch.  This module
//      computes the terminal count from the actual clock frequency instead.
//
//  TIMING
//      75 MHz * 20 ms = 1,500,000 counts, which needs 21 bits.  CNT_W is
//      derived from CLK_HZ so the counter is always wide enough.
//
//  OUTPUT
//      O_pressed is a clean LEVEL, high while the key is held down (the key
//      input is active LOW).  The level only changes after the input has been
//      stable for the whole debounce interval, so a bouncing press produces
//      exactly one transition.
//
//  The synchroniser is two flip-flops, which is what a single-bit input from
//  an asynchronous pin needs before it can be used in this clock domain.
//=============================================================================
`timescale 1ns / 1ps

module key_debounce #(
    parameter integer CLK_HZ      = 75_000_000,
    parameter integer DEBOUNCE_MS = 20
)(
    input  wire I_clk,
    input  wire I_rst_n,
    input  wire I_key_n,        // raw key, active low
    output wire O_pressed       // debounced level, high = pressed
);

    // counts for the requested interval; at least one
    localparam integer CNT_MAX = (CLK_HZ / 1000) * DEBOUNCE_MS;
    localparam integer CNT_W   = $clog2(CNT_MAX + 1);

    reg sync0, sync1;
    reg [CNT_W-1:0] cnt;
    reg stable;                 // debounced, active low like the input

    always @(posedge I_clk) begin
        sync0 <= I_key_n;
        sync1 <= sync0;
    end

    always @(posedge I_clk or negedge I_rst_n) begin
        if(!I_rst_n) begin
            cnt    <= {CNT_W{1'b0}};
            stable <= 1'b1;                 // released
        end
        else if(sync1 != stable) begin
            // input differs from the settled value: wait for it to persist
            if(cnt == CNT_MAX[CNT_W-1:0] - 1'b1) begin
                stable <= sync1;
                cnt    <= {CNT_W{1'b0}};
            end
            else
                cnt <= cnt + 1'b1;
        end
        else
            cnt <= {CNT_W{1'b0}};           // input agrees again, restart
    end

    assign O_pressed = ~stable;

endmodule
