clear all;
close all;
clc;

Range = [10,20,30,40,50,60,70,80];
color = jet(8);
figure(1);
set(gcf, 'Position',  [100, 100, 1200, 450]);
for RangeIdx = 1:8
    for TestIdx = 1:10
           path = sprintf('./%dcm/scope_%d.csv',Range(RangeIdx),TestIdx);
           M = csvread(path,2,0);
           time = M(:,1);
           Vc = M(:,2);
           hold on;
           fig = plot(time,Vc);
           fig.Color = color(RangeIdx,:);
           xlim([time(1),time(length(time))]);
           xL = get(gca, 'XLim');
           plot(xL, [1.8 1.8], 'r--')
           yL = get(gca, 'YLim');
           plot([0.030 0.030],yL, 'k--')
    end
end
set(findall(gcf,'-property','FontSize'),'FontSize',18);
xlabel('Time (s)','FontSize',20);
ylabel('Charge Pump output Voltage (V)','FontSize',20);
ylim([0 4]);
xlim([0 0.1]);
print(gcf, 'F1.pdf', '-dpdf', '-bestfit')