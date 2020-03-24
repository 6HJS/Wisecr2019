clear all;
close all;
clc;

Range = [10,20,30,40,50,60,70,80];
color = jet(8);
figure(2);
set(gcf, 'Position',  [100, 100, 1200, 450]);
for RangeIdx = 1:8
    pos = [];
    for TestIdx = 1:10
           pathV = sprintf('./%dcm/scope_%d_1.csv',Range(RangeIdx),TestIdx);
           pathS = sprintf('./%dcm/scope_%d_3.csv',Range(RangeIdx),TestIdx);
           M = csvread(pathV,2,0);
           N = csvread(pathS,2,0);
           time = M(:,1);
           Vc = M(:,2);
           if (RangeIdx == 1)
              Vc = Vc+0.4; 
           end
           if (RangeIdx == 2)
              Vc = Vc-0.2; 
           end
           if (RangeIdx == 3)
              Vc = Vc+0.25; 
           end
           Sig = N(:,2)/10;
           hold on;
           fig = plot(time,Vc);
           fig.Color = color(RangeIdx,:);
           xlim([time(1),time(length(time))]);
           xL = get(gca, 'XLim');
           plot(xL, [1.8 1.8], 'r--')
           yL = get(gca, 'YLim');
           plot([0.030 0.030],yL, 'k--')
           
           negth = 2;
           ctr = 0;
           for tidx = 1:length(Sig)-3
               if((Sig(tidx) > 1.8 && Sig(tidx+1) > 1.8) && (Sig(tidx+2) <= 1.8 && Sig(tidx+3) <= 1.8))
                   ctr = ctr+1;
                   if(ctr >= negth)
                       pos = [pos time(tidx)];
                       break;
                   end
               end
           end
    end
                       pos_bar = mean(pos);
%                        fig = plot(time,Sig);
%                        fig.Color = color(RangeIdx,:);
%                        yL = get(gca, 'YLim');
                       h = plot([pos_bar pos_bar],[0 1.8],'-.');
                       h.Color = color(RangeIdx,:);
                       tpos = pos'
end
set(findall(gcf,'-property','FontSize'),'FontSize',18);
xlabel('Time (s)','FontSize',20);
ylabel('Charge Pump output Voltage (V)','FontSize',20);
ylim([0 4]);
xlim([0.1 0.2]);
print(gcf, 'F2.pdf', '-dpdf', '-bestfit')
