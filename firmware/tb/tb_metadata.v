`timescale 1ns/1ps
module tb_metadata;
reg clk=0,rst=0,frame=0;wire tx;
integer i;integer f;
always #5 clk=~clk;
spot_uart #(.EXT_STATUS(1),.CLK_FRE(460800),.BAUD(115200)) dut(.I_clk(clk),.I_rst_n(rst),.I_frame(frame),.I_mode(2'd3),.I_agc(1'b1),.I_gain_ex(8'd64),.I_xc(20'd161898),.I_yc(20'd92116),.I_dx(-20'sd2099),.I_dy(20'sd26),.I_n(20'd1234),.O_txd(tx),.O_busy());
always @(posedge clk)if(dut.tx_start)$fwrite(f,"%c",dut.tx_data);
initial begin f=$fopen("metadata.txt","wb");repeat(5)@(negedge clk);rst=1;@(negedge clk);frame=1;@(negedge clk);frame=0;repeat(5000)@(negedge clk);$fclose(f);$finish;end
endmodule
