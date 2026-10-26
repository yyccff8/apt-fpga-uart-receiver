`timescale 1ns/1ps
// Profiles: 0 legacy full RGB888/115200, 1 full RGB888/3M,
// 2 centered 4x decimation to RGB565/3M. All read the existing frozen DDR copy.
module uart_image_sender #(
parameter [24:0] SNAP_BASE=25000000,parameter integer SRC_W=1280,SRC_H=720,CLK_HZ=66666667
)(input wire I_clk,I_rst_n,I_start,input wire[1:0] I_profile,
output wire O_busy,output reg O_done,output wire O_txd,output wire O_rd_en,
output reg[24:0] O_addr,input wire I_ready,I_rd_valid,input wire[127:0] I_rd_data,output wire[7:0] O_progress);
localparam integer STRIDE=SRC_W*3/16,WORDS=STRIDE*SRC_H,QW=SRC_W/4,QH=SRC_H/4;
localparam IDLE=0,HDR=1,PREP=2,RDA=3,WA=4,RDB=5,WB=6,RAW=7,LO=8,HI=9,ADV=10,CL=11,CH=12,DRAIN=13;
reg[3:0] state,header_index,byte_index;
reg[1:0] profile;
reg[17:0] word_index;
reg[8:0] x;
reg[7:0] y,base_word;
reg[24:0] row_base;
reg[127:0] buffer;
reg[7:0] split_r,split_g,tx_data;
reg[15:0] pixel,crc;
reg tx_start;wire tx_busy;
assign O_busy=state!=IDLE;
assign O_rd_en=(state==RDA||state==RDB);
assign O_progress=word_index[17:10];
wire[15:0] w16=(profile==2)?QW:SRC_W,h16=(profile==2)?QH:SRC_H;
function[7:0] header;input[3:0] i;begin case(i)
0:header="H";1:header="X";2:header=(profile==2)?"P":"F";3:header=(profile==2)?"1":"2";
4:header=w16[7:0];5:header=w16[15:8];6:header=h16[7:0];7:header=h16[15:8];
8:header=(profile==2)?8'd2:STRIDE%256;default:header=(profile==2)?8'd0:STRIDE/256;
endcase end endfunction
function[15:0] crc_byte;input[15:0] c;input[7:0] b;reg[15:0] v;integer i;
begin v=c^{b,8'd0};for(i=0;i<8;i=i+1)v=v[15]?(v<<1)^16'h1021:v<<1;crc_byte=v;end endfunction
wire[7:0] raw_byte=buffer[byte_index*8+:8];
wire[7:0] source_word=base_word+((x[1:0]==0)?8'd0:(x[1:0]==3)?8'd2:8'd1);
uart_tx_select #(.CLK_HZ(CLK_HZ)) u_tx(.I_clk(I_clk),.I_rst_n(I_rst_n),.I_fast(profile!=0),.I_data(tx_data),.I_valid(tx_start),.O_txd(O_txd),.O_busy(tx_busy));
always @(posedge I_clk or negedge I_rst_n)begin
 if(!I_rst_n)begin state<=IDLE;profile<=0;header_index<=0;byte_index<=0;word_index<=0;x<=0;y<=0;base_word<=0;row_base<=0;
 O_addr<=0;buffer<=0;split_r<=0;split_g<=0;tx_data<=0;pixel<=0;crc<=16'hffff;tx_start<=0;O_done<=0;end
 else begin tx_start<=0;O_done<=0;
 case(state)
 IDLE:if(I_start)begin profile<=I_profile;state<=HDR;header_index<=0;word_index<=0;x<=0;y<=0;base_word<=0;row_base<=SNAP_BASE+STRIDE*16;crc<=16'hffff;end
 HDR:if(!tx_busy&&!tx_start)begin tx_data<=header(header_index);tx_start<=1;crc<=crc_byte(crc,header(header_index));if(header_index==9)state<=PREP;else header_index<=header_index+1'b1;end
 PREP:begin
  if(profile!=2&&word_index==WORDS)state<=CL;
  else if(profile==2)begin O_addr<=row_base+{14'd0,source_word,3'b000};state<=RDA;end
  else begin O_addr<=SNAP_BASE+{4'd0,word_index,3'b000};state<=RDA;end
 end
 RDA:if(I_ready)state<=WA;
 WA:if(I_rd_valid)begin
  if(profile!=2)begin buffer<=I_rd_data;byte_index<=15;state<=RAW;end
  else case(x[1:0])
   0:begin pixel<={I_rd_data[79:75],I_rd_data[71:66],I_rd_data[63:59]};state<=LO;end
   1:begin pixel<={I_rd_data[111:107],I_rd_data[103:98],I_rd_data[95:91]};state<=LO;end
   2:begin split_r<=I_rd_data[15:8];split_g<=I_rd_data[7:0];O_addr<=O_addr+25'd8;state<=RDB;end
   3:begin pixel<={I_rd_data[47:43],I_rd_data[39:34],I_rd_data[31:27]};state<=LO;end
  endcase
 end
 RDB:if(I_ready)state<=WB;
 WB:if(I_rd_valid)begin pixel<={split_r[7:3],split_g[7:2],I_rd_data[127:123]};state<=LO;end
 RAW:if(!tx_busy&&!tx_start)begin tx_data<=raw_byte;tx_start<=1;crc<=crc_byte(crc,raw_byte);if(byte_index==0)begin word_index<=word_index+1'b1;state<=PREP;end else byte_index<=byte_index-1'b1;end
 LO:if(!tx_busy&&!tx_start)begin tx_data<=pixel[7:0];tx_start<=1;crc<=crc_byte(crc,pixel[7:0]);state<=HI;end
 HI:if(!tx_busy&&!tx_start)begin tx_data<=pixel[15:8];tx_start<=1;crc<=crc_byte(crc,pixel[15:8]);state<=ADV;end
 ADV:if(x==QW-1)begin
  x<=0;base_word<=0;row_base<=row_base+STRIDE*32;
  if(y==QH-1)state<=CL;else begin y<=y+1'b1;state<=PREP;end
 end else begin x<=x+1'b1;if(x[1:0]==3)base_word<=base_word+8'd3;state<=PREP;end
 CL:if(!tx_busy&&!tx_start)begin tx_data<=crc[7:0];tx_start<=1;state<=CH;end
 CH:if(!tx_busy&&!tx_start)begin tx_data<=crc[15:8];tx_start<=1;state<=DRAIN;end
 DRAIN:if(!tx_busy&&!tx_start)begin state<=IDLE;O_done<=1;end
 default:state<=IDLE;
 endcase end
end
endmodule

// Profile is latched for the entire image, including its final stop bit.
module uart_tx_select #(parameter integer CLK_HZ=66666667)(input wire I_clk,I_rst_n,I_fast,input wire[7:0] I_data,input wire I_valid,output reg O_txd,output wire O_busy);
localparam integer FAST=CLK_HZ/3000000,SLOW=CLK_HZ/115200;
reg[3:0] state;reg[15:0] count;reg[2:0] bitno;reg[7:0] data;reg fast;
wire[15:0] limit=fast?FAST-1:SLOW-1;
assign O_busy=state!=0;
always @(posedge I_clk or negedge I_rst_n)begin
 if(!I_rst_n)begin state<=0;count<=0;bitno<=0;data<=0;fast<=0;O_txd<=1;end
 else case(state)
 0:begin O_txd<=1;if(I_valid)begin data<=I_data;fast<=I_fast;O_txd<=0;count<=0;state<=1;end end
 1:if(count==limit)begin count<=0;bitno<=0;O_txd<=data[0];state<=2;end else count<=count+1'b1;
 2:if(count==limit)begin count<=0;if(bitno==7)begin O_txd<=1;state<=3;end else begin bitno<=bitno+1'b1;O_txd<=data[bitno+1'b1];end end else count<=count+1'b1;
 3:if(count==limit)begin state<=0;count<=0;end else count<=count+1'b1;
 default:state<=0;
 endcase
end
endmodule
