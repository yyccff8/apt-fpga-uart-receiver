`timescale 1ns/1ps
module tb_commands;
reg hd=0,ddr=0,rst=0,rx=1,pbusy=1,sbusy=0,gbusy=0,done=0;
integer requests=0;
always #5 hd=~hd;always #7.5 ddr=~ddr;
apt_transport dut();
always @(posedge hd)if(dut.S_pc_grab_request)requests=requests+1;
task command(input[7:0]c);integer i;begin
@(negedge hd);rx=0;repeat(25)@(negedge hd);
for(i=0;i<8;i=i+1)begin rx=c[i];repeat(25)@(negedge hd);end
rx=1;repeat(30)@(negedge hd);
end endtask
task completion;begin
@(negedge ddr);sbusy=0;gbusy=0;done=1;@(negedge ddr);done=0;repeat(12)@(negedge hd);
end endtask
initial begin
force dut.S_hdmi_pixel_clk=hd;force dut.S_csi_rx_clk=hd;force dut.S_ddr_clk=ddr;force dut.S_rst_n=rst;force dut.I_uart_rxd=rx;
force dut.S_preview_busy=pbusy;force dut.S_spot_hi_busy=0;force dut.S_spot_lo_busy=0;force dut.S_spot_uart_go=0;
force dut.S_send_busy=sbusy;force dut.S_send_go=0;force dut.S_send_done=done;force dut.S_grab_busy=gbusy;
repeat(5)@(negedge hd);rst=1;command("Q");
if(dut.S_quality_requested!==1||dut.S_preview_mode!==1)$fatal(1,"Q while fast frame busy");
pbusy=0;repeat(15)@(negedge hd);
if(requests!=1||dut.S_capture_profile_hd!=2||!dut.S_usb_reserved)$fatal(1,"quality request and UART reservation");
sbusy=1;command("H");if(dut.S_capture_profile_hd!=2||!dut.S_pc_snapshot_pending)$fatal(1,"H must not change in-flight Q profile");
completion;dut.S_quality_cooldown=0;repeat(15)@(negedge hd);
if(requests!=2||dut.S_capture_profile_hd!=1||!dut.S_usb_reserved)$fatal(1,"HQ capture profile");
sbusy=1;completion;repeat(20)@(negedge hd);
if(requests!=2||dut.S_preview_mode!=0||dut.S_usb_fast_mode!=1)$fatal(1,"hold HQ view with fast telemetry");
command("P");repeat(10)@(negedge hd);if(dut.S_preview_mode!=1||dut.S_quality_requested!=0)$fatal(1,"resume fast preview");
$display("PASS: real UART Q/H/P, packet-boundary mode switching, reserved image transfers and HQ pause/resume");$finish;
end
initial begin #1000000;$fatal(1,"command timeout");end
endmodule
