// Four clients share one request address. Background reads are serialized
// after all display responses drain, so no read-owner tag RAM is needed.
// Video writes have a one-cycle output pipeline and must always be forwarded:
// mc_to_user_interface reserves eight FIFO entries below almost-full.
module grab_ddr_arbiter (
    input wire I_clk, I_rst_n,
    input wire I_vi_busy, I_vi_wr,
    input wire [24:0] I_vi_addr,
    input wire [127:0] I_vi_data,
    input wire I_vo_busy, I_vo_rd,
    input wire [24:0] I_vo_addr,
    output wire O_vo_ready, O_vo_valid,
    input wire I_gr_rd, I_gr_wr,
    input wire [24:0] I_gr_addr,
    input wire [127:0] I_gr_data,
    output wire O_gr_ready, O_gr_valid,
    input wire I_send_rd,
    input wire [24:0] I_send_addr,
    output wire O_send_ready, O_send_valid,
    input wire I_ready, I_rd_valid,
    output wire O_wr, O_rd,
    output wire [24:0] O_addr,
    output wire [127:0] O_data
);
    reg [5:0] display_pending;
    reg [3:0] display_quota;
    // A held background request reserves a drain slot after at most fifteen
    // display requests. Without this bound, repeated display bursts can keep
    // the response queue nonempty forever and starve a snapshot.
    wire background_request = I_gr_rd || I_gr_wr || I_send_rd;
    wire background_due = background_request &&
                          ((display_quota == 4'd15) || !I_vo_busy);
    reg [1:0] background_owner; // 0 none, 1 snapshot, 2 UART
    wire display_return = I_rd_valid && (background_owner == 0) && (display_pending != 0);
    // video_out starts a 240-word burst with at least 300 free FIFO entries.
    // Keep old in-flight responses below its 60-word guard, even if the DDR
    // controller stalls much longer than the original fixed-latency estimate.
    // Maximum occupancy at a new burst: 211 present + 32 old + 240 new = 483.
    assign O_vo_ready = I_ready && !I_vi_busy && !I_vi_wr &&
                        (background_owner == 0) && (display_pending < 6'd32) && !background_due;
    wire display_accept = I_vo_rd && O_vo_ready;
    wire background_slot = I_ready && !I_vi_busy && !I_vi_wr && background_due && (display_pending == 0) && (background_owner == 0);
    assign O_gr_ready = background_slot;
    assign O_send_ready = background_slot && !I_gr_rd && !I_gr_wr;
    wire grab_write = I_gr_wr && O_gr_ready;
    wire grab_read = I_gr_rd && O_gr_ready && !I_gr_wr;
    wire send_read = I_send_rd && O_send_ready;
    assign O_wr = I_vi_wr || grab_write;
    assign O_rd = display_accept || grab_read || send_read;
    assign O_addr = I_vi_wr ? I_vi_addr :
                    display_accept ? I_vo_addr :
                    (grab_write || grab_read) ? I_gr_addr :
                    send_read ? I_send_addr : 25'd0;
    assign O_data = I_vi_wr ? I_vi_data : I_gr_data;
    assign O_vo_valid = display_return;
    assign O_gr_valid = I_rd_valid && (background_owner == 1);
    assign O_send_valid = I_rd_valid && (background_owner == 2);
    always @(posedge I_clk or negedge I_rst_n) begin
        if (!I_rst_n) begin
            display_pending <= 0;
            display_quota <= 0;
            background_owner <= 0;
        end else begin
            if (grab_write || grab_read || send_read) display_quota <= 0;
            else if (display_accept && display_quota != 4'd15)
                display_quota <= display_quota + 1'b1;
            case ({display_accept, display_return})
                2'b10: display_pending <= display_pending + 1'b1;
                2'b01: display_pending <= display_pending - 1'b1;
                default: display_pending <= display_pending;
            endcase
            if (I_rd_valid && background_owner != 0) background_owner <= 0;
            if (grab_read) background_owner <= 1;
            else if (send_read) background_owner <= 2;
        end
    end
endmodule
