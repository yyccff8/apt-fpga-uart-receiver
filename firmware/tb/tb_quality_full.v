`timescale 1ns/1ps
module tb_image_sender;
parameter integer PROFILE=2;
localparam SW=1280,SH=720,STRIDE=240,WORDS=172800,COUNT=115212,C=2;
reg clk=0,rst=0,start=0,ready=0,rv=0,pend=0;
wire busy,done,tx,rd;wire[24:0]addr;reg[24:0] pending_addr;reg[127:0]data=0;
reg[127:0]memory[0:WORDS-1];integer i,j,k,pixel,x,y,channel,delay=0,cyc=0,f;reg[7:0]b;
always #7.5 clk=~clk;
uart_image_sender #(.SRC_W(SW),.SRC_H(SH),.CLK_HZ(6000000)) dut(.I_clk(clk),.I_rst_n(rst),.I_start(start),.I_profile(PROFILE[1:0]),.O_busy(busy),.O_done(done),.O_txd(tx),.O_rd_en(rd),.O_addr(addr),.I_ready(ready),.I_rd_valid(rv),.I_rd_data(data),.O_progress());
always @(posedge clk)begin
 cyc<=cyc+1;ready<=cyc%7!=0;rv<=0;
 if(rd&&ready)begin if(pend)$fatal(1,"more than one DDR read outstanding");pending_addr<=addr;pend<=1;delay<=5;end
 else if(pend)begin if(delay!=0)delay<=delay-1;else begin data<=memory[(pending_addr-25000000)/8];rv<=1;pend<=0;end end
end
initial begin
for(i=0;i<WORDS;i=i+1)for(j=0;j<16;j=j+1)begin
pixel=(i*16+j)/3;x=pixel%SW;y=pixel/SW;channel=(i*16+j)%3;
case(channel)0:memory[i][127-j*8-:8]=x*7+y;1:memory[i][127-j*8-:8]=y*11+x;2:memory[i][127-j*8-:8]=x^y;endcase
end
repeat(5)@(negedge clk);rst=1;@(negedge clk);start=1;@(negedge clk);start=0;
end
initial begin
f=$fopen("quality_full.hex","w");
for(k=0;k<COUNT;k=k+1)begin
@(negedge tx);repeat(C+C/2)@(negedge clk);
for(j=0;j<8;j=j+1)begin b[j]=tx;repeat(C)@(negedge clk);end
if(tx!==1)$fatal(1,"stop bit");$fwrite(f,"%02x\n",b);
end
$fclose(f);$display("PASS image sender profile=%0d bytes=%0d",PROFILE,COUNT);$finish;
end
initial begin #200000000;$fatal(1,"sender timeout");end
endmodule
