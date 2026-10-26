// Request/completion toggles cross clock domains. Freeze starts one DDR
// cycle before frame_grab latches the source slot; it ends after the copy.
module grab_request_control (
    input wire I_rst_n, I_hdmi_clk, I_ddr_clk, I_request,
    input wire I_grab_busy, I_send_busy, I_send_done,
    output wire O_start, O_freeze
);
    reg request_toggle;
    reg complete_toggle;
    reg [2:0] complete_sync, request_sync;
    reg request_seen, pending;
    always @(posedge I_hdmi_clk or negedge I_rst_n) begin
        if (!I_rst_n) begin
            request_toggle <= 0;
            complete_sync <= 0;
        end else begin
            complete_sync <= {complete_sync[1:0],complete_toggle};
            if (I_request && request_toggle == complete_sync[2])
                request_toggle <= ~request_toggle;
        end
    end
    assign O_start = pending && !I_grab_busy && !I_send_busy;
    assign O_freeze = pending || I_grab_busy;
    always @(posedge I_ddr_clk or negedge I_rst_n) begin
        if (!I_rst_n) begin
            request_sync <= 0;
            request_seen <= 0;
            complete_toggle <= 0;
            pending <= 0;
        end else begin
            request_sync <= {request_sync[1:0],request_toggle};
            if (request_sync[2] != request_seen && !I_grab_busy && !I_send_busy && !pending) begin
                request_seen <= request_sync[2];
                pending <= 1;
            end else if (O_start) pending <= 0;
            if (I_send_done) complete_toggle <= request_seen;
        end
    end
endmodule
