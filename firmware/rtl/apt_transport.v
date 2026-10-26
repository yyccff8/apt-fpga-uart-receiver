`timescale 1ns/1ps
// Transport-only extraction from the v18 board project. Fixed 1280x720 RGB888.
// No camera/ISP, centroid algorithm, DDR controller or HDMI implementation.
module apt_transport #(parameter [24:0] SNAP_BASE=25'd25000000)(
 input wire S_rst_n,S_hdmi_pixel_clk,S_csi_rx_clk,S_ddr_clk,
 input wire I_uart_rxd,I_sw4,I_legacy_grab_pulse,
 input wire [95:0] I_rgb,
 input wire I_valid,I_sof,I_last,
 input wire I_telemetry_valid,
 input wire [19:0] I_xc,I_yc,I_n,
 input wire signed [19:0] I_dx,I_dy,
 input wire [1:0] I_mode,input wire I_agc,input wire [7:0] I_gain_ex,
 input wire [24:0] S_grab_src,
 input wire S_gr_ready,S_gr_rd_valid,S_send_ready,S_send_rd_valid,
 input wire [127:0] S_ddr_user_rd_data,
 output wire O_uart_txd,O_freeze,
 output wire S_grab_rd_en,S_grab_wr_en,
 output wire [24:0] S_grab_addr,S_send_addr,
 output wire [127:0] S_grab_wr_data,
 output wire O_send_rd_en,O_image_busy
);
 wire S_sw2_rise=I_legacy_grab_pulse;
 wire S_grab_busy,S_grab_done,S_grab_freeze,S_uart_txd,S_spot_txd;
 assign O_freeze=S_grab_freeze;
    reg S_send_go;
    wire        S_send_busy, S_send_done, S_send_rd_en;
    reg S_spot_uart_go;
    wire [95:0] S_preview_data;
    wire S_preview_valid,S_preview_sof,S_preview_last;
    wire S_preview_busy,S_preview_txd,S_spot_hi_txd,S_spot_hi_busy,S_spot_lo_busy;
    reg S_preview_requested,S_preview_mode;
    reg S_usb_fast_requested,S_usb_fast_mode,S_quality_requested,S_pc_snapshot_pending,S_usb_reserved;
    reg S_pc_grab_request;
    reg [20:0] S_quality_cooldown;
    reg [1:0] S_capture_profile_hd,S_capture_profile_s1,S_capture_profile_s2;
    reg S_image_done_toggle;reg [2:0] S_image_done_sync;reg S_image_done_seen;
    wire S_image_done_event=(S_image_done_sync[2]!=S_image_done_seen);
    wire S_command_valid;wire[7:0] S_command_data;
    uart_command_rx u_usb_commands(.I_clk(S_hdmi_pixel_clk),.I_rst_n(S_rst_n),.I_rxd(I_uart_rxd),.O_valid(S_command_valid),.O_data(S_command_data));
    always @(posedge S_ddr_clk or negedge S_rst_n)
        if(!S_rst_n)begin S_image_done_toggle<=0;S_capture_profile_s1<=0;S_capture_profile_s2<=0;end
        else begin
            if(S_send_done)S_image_done_toggle<=~S_image_done_toggle;
            S_capture_profile_s1<=S_capture_profile_hd;S_capture_profile_s2<=S_capture_profile_s1;
        end
    always @(posedge S_hdmi_pixel_clk or negedge S_rst_n)
        if(!S_rst_n)begin S_image_done_sync<=0;S_image_done_seen<=0;end
        else begin S_image_done_sync<={S_image_done_sync[1:0],S_image_done_toggle};S_image_done_seen<=S_image_done_sync[2];end
    wire S_sw4_pressed;
    reg [22:0] S_sw4_init;
    reg S_sw4_last;
    reg [1:0] S_preview_mode_csi;
    reg [2:0] S_legacy_busy_s1,S_legacy_busy_s2;
    key_debounce #(.CLK_HZ(75000000),.DEBOUNCE_MS(20)) u_sw4_deb(
        .I_clk(S_hdmi_pixel_clk),.I_rst_n(S_rst_n),.I_key_n(I_sw4),.O_pressed(S_sw4_pressed));
    always @(posedge S_hdmi_pixel_clk or negedge S_rst_n) begin
        if(!S_rst_n)begin
            S_sw4_init<=0;S_sw4_last<=0;S_preview_requested<=1;S_usb_fast_requested<=1;
            S_quality_requested<=0;S_pc_snapshot_pending<=0;S_usb_reserved<=0;S_pc_grab_request<=0;S_capture_profile_hd<=0;S_quality_cooldown<=0;
        end else begin
            S_pc_grab_request<=0;
            if(S_quality_cooldown!=0)S_quality_cooldown<=S_quality_cooldown-1'b1;
            if(S_image_done_event)begin S_usb_reserved<=0;S_quality_cooldown<=21'd1500000;end
            if(!S_usb_reserved&&!S_quality_requested&&!S_pc_snapshot_pending)S_capture_profile_hd<=0;
            if(!S_usb_reserved&&!S_preview_mode&&!S_preview_busy&&!S_spot_hi_busy&&!S_spot_lo_busy&&
               S_legacy_busy_s2==0&&!S_spot_uart_go&&S_quality_cooldown==0&&(S_pc_snapshot_pending||S_quality_requested))begin
                S_capture_profile_hd<=S_pc_snapshot_pending?2'd1:2'd2;
                S_pc_snapshot_pending<=0;S_usb_reserved<=1;S_pc_grab_request<=1;
            end
            if(S_sw4_init<23'd7500000)begin S_sw4_init<=S_sw4_init+1'b1;S_sw4_last<=S_sw4_pressed;end
            else if(S_sw4_pressed!=S_sw4_last)begin
                S_sw4_last<=S_sw4_pressed;S_preview_requested<=~S_preview_requested;S_usb_fast_requested<=~S_preview_requested;
                S_quality_requested<=0;S_pc_snapshot_pending<=0;
            end
            if(S_command_valid)case(S_command_data)
            "P":begin S_preview_requested<=1;S_usb_fast_requested<=1;S_quality_requested<=0;S_pc_snapshot_pending<=0;end
            "Q":begin S_preview_requested<=0;S_usb_fast_requested<=1;S_quality_requested<=1;S_pc_snapshot_pending<=0;S_quality_cooldown<=0;end
            "H":begin S_preview_requested<=0;S_usb_fast_requested<=1;S_quality_requested<=0;S_pc_snapshot_pending<=1;S_quality_cooldown<=0;end
            endcase
        end
    end
    always @(posedge S_csi_rx_clk or negedge S_rst_n)
        if(!S_rst_n)S_preview_mode_csi<=2'b11;
        else S_preview_mode_csi<={S_preview_mode_csi[0],S_preview_mode};
    wire S_grab_start;
    grab_request_control u_grab_request_control (
        .I_rst_n(S_rst_n), .I_hdmi_clk(S_hdmi_pixel_clk),
        .I_ddr_clk(S_ddr_clk), .I_request((S_sw2_rise&&!S_preview_mode&&!S_usb_fast_mode)||S_pc_grab_request),
        .I_grab_busy(S_grab_busy), .I_send_busy(S_send_busy),
        .I_send_done(S_send_done), .O_start(S_grab_start),
        .O_freeze(S_grab_freeze)
    );
    // All source changes occur only with both transmitters fully idle.
    always @(posedge S_hdmi_pixel_clk or negedge S_rst_n) begin
        if(!S_rst_n)begin S_preview_mode<=1;S_usb_fast_mode<=1;S_legacy_busy_s1<=0;S_legacy_busy_s2<=0;end
        else begin
            S_legacy_busy_s1<={S_send_busy,S_send_go,S_grab_busy};S_legacy_busy_s2<=S_legacy_busy_s1;
            if(!S_preview_busy&&!S_spot_hi_busy&&!S_spot_lo_busy&&S_legacy_busy_s2==0&&!S_usb_reserved)
                begin S_preview_mode<=S_preview_requested;S_usb_fast_mode<=S_usb_fast_requested;end
        end
    end
    assign O_uart_txd = S_send_busy ? S_uart_txd :
                       S_usb_fast_mode ? (S_preview_busy?S_preview_txd:S_spot_hi_txd) : S_spot_txd;

    frame_grab #(
        .DST_BASE   (SNAP_BASE),
        .WORDS      (172800),          // 1280*3/16 * 720
        .AW         (25)
    ) u_frame_grab (
        .I_clk      (S_ddr_clk),
        .I_rst_n    (S_rst_n),
        .I_start    (S_grab_start),
        .I_src_base (S_grab_src),
        .O_busy     (S_grab_busy),
        .O_done     (S_grab_done),
        .O_rd_en    (S_grab_rd_en),
        .O_wr_en    (S_grab_wr_en),
        .O_addr     (S_grab_addr),
        .O_wr_data  (S_grab_wr_data),
        .I_ready    (S_gr_ready),
        .I_rd_valid (S_gr_rd_valid),
        .I_rd_data  (S_ddr_user_rd_data)
    );

    // ---- sender starts once the snapshot is complete ---------------------
    always @(posedge S_ddr_clk or negedge S_rst_n) begin
        if(!S_rst_n)          S_send_go <= 1'b0;
        else if(S_grab_done)  S_send_go <= 1'b1;
        else if(S_send_busy)  S_send_go <= 1'b0;
    end

    uart_image_sender #(.SNAP_BASE(SNAP_BASE),.SRC_W(1280),.SRC_H(720),.CLK_HZ(66666667)) u_uart_sender (
        .I_profile(S_capture_profile_s2),
        .I_clk      (S_ddr_clk),
        .I_rst_n    (S_rst_n),
        .I_start    (S_send_go),
        .O_busy     (S_send_busy),
        .O_done     (S_send_done),
        .O_txd      (S_uart_txd),
        .O_rd_en    (S_send_rd_en),
        .O_addr     (S_send_addr),
        .I_ready    (S_send_ready),
        .I_rd_valid (S_send_rd_valid),
        .I_rd_data  (S_ddr_user_rd_data),
        .O_progress ()
    );

 assign O_send_rd_en=S_send_rd_en;
 assign O_image_busy=S_usb_reserved||S_grab_busy||S_send_busy;
 assign S_preview_data=I_rgb;
 assign S_preview_valid=I_valid;
 assign S_preview_sof=I_sof;
 assign S_preview_last=I_last;
 always @(posedge S_hdmi_pixel_clk or negedge S_rst_n)
  if(!S_rst_n) S_spot_uart_go<=0; else S_spot_uart_go<=I_telemetry_valid;
 spot_uart #(.CLK_FRE(75000000),.BAUD(115200)) u_spot_uart(
  .I_clk(S_hdmi_pixel_clk),.I_rst_n(S_rst_n),
  .I_frame(S_spot_uart_go&&!S_usb_fast_mode&&!S_usb_reserved),
  .I_xc(I_xc),.I_yc(I_yc),.I_dx(I_dx),.I_dy(I_dy),.I_n(I_n),
  .I_mode(I_mode),.I_agc(I_agc),.I_gain_ex(I_gain_ex),
  .O_txd(S_spot_txd),.O_busy(S_spot_lo_busy));
 spot_uart #(.EXT_STATUS(1),.CLK_FRE(75000000),.BAUD(3000000)) u_spot_uart_hi(
  .I_clk(S_hdmi_pixel_clk),.I_rst_n(S_rst_n),
  .I_frame(S_spot_uart_go&&S_usb_fast_mode&&!S_preview_busy&&!S_usb_reserved&&!S_legacy_busy_s2[2]),
  .I_xc(I_xc),.I_yc(I_yc),.I_dx(I_dx),.I_dy(I_dy),.I_n(I_n),
  .I_mode(I_mode),.I_agc(I_agc),.I_gain_ex(I_gain_ex),
  .O_txd(S_spot_hi_txd),.O_busy(S_spot_hi_busy));
 usb_preview u_usb_preview(
  .I_rst_n(S_rst_n),.I_capture_clk(S_csi_rx_clk),.I_capture_enable(S_preview_mode_csi[1]),
  .I_sof(S_preview_sof),.I_valid(S_preview_valid),.I_last(S_preview_last),.I_rgb(S_preview_data),
  .I_tx_clk(S_hdmi_pixel_clk),.I_tx_enable(S_preview_mode),
  .I_allow_start(S_preview_mode&&S_preview_requested&&!S_spot_hi_busy&&!S_spot_uart_go),
  .O_txd(S_preview_txd),.O_busy(S_preview_busy));
endmodule
