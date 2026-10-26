`timescale 1ns/1ps
module uart_command_rx #(parameter integer CLK_HZ=75000000,BAUD=3000000)(
input wire I_clk,I_rst_n,I_rxd,output reg O_valid,output reg[7:0] O_data);
localparam integer C=CLK_HZ/BAUD;
reg[1:0] sync;reg[1:0] state;reg[15:0] count;reg[2:0] bitno;reg[7:0] data;
always @(posedge I_clk or negedge I_rst_n)begin
 if(!I_rst_n)begin sync<=3;state<=0;count<=0;bitno<=0;data<=0;O_data<=0;O_valid<=0;end
 else begin sync<={sync[0],I_rxd};O_valid<=0;
 case(state)
 0:if(!sync[1])begin count<=C/2-1;state<=1;end
 1:if(count!=0)count<=count-1'b1;else if(!sync[1])begin count<=C-1;bitno<=0;state<=2;end else state<=0;
 2:if(count!=0)count<=count-1'b1;else begin data[bitno]<=sync[1];count<=C-1;if(bitno==7)state<=3;else bitno<=bitno+1'b1;end
 3:if(count!=0)count<=count-1'b1;else begin if(sync[1])begin O_data<=data;O_valid<=1;end state<=0;end
 endcase end
end
endmodule
