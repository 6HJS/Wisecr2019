clear all;
close all;
clc;

Data = [%0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11;
        %0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11;
        %0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11,0.11;
        0.0457,0.0383,0.0376,0.101,0.0465,0.0555,0.0543,0.0491,0.0509,0.0498;
        0.0342,0.03092,0.02326,0.03166,0.03166,0.03276,0.03358,0.03356,0.03322,0.03322;
        0.02282,0.02132,0.02536,0.02458,0.03092,0.02932,0.02426,0.02128,0.02706,0.0273;
        0.02202,0.02816,0.02644,0.02754,0.02604,0.0288,0.02706,0.03002,0.0245,0.02902;
        0.02456,0.02368,0.02144,0.02566,0.02422,0.02922,0.02708,0.02442,0.0288,0.0257;
]';

Label = {'10','20','30','40','50','60','70','80'};
Label = Label(4:8);

boxplot(Data,'Labels',Label);
ylabel('IPC duration (s)');
xlabel('Operational range (cm)')
figure();
for lineIdx = 1:length(Data(1,:))
    subplot(1,length(Data(1,:)),lineIdx);
    Data_line = Data(:,lineIdx)*16e6;%clk = time*freq
    if(lineIdx==4-3) %manually adjust bin width
        num = 30;
    elseif(lineIdx==5-3)
        num = 6;
    elseif(lineIdx==6-3)
        num = 5;
    elseif(lineIdx==7-3)
        num = 5;
    elseif(lineIdx==8-3)
        num = 5;
    else
        num = 20;
    end
    histfit(Data_line,num);
    xlim([0,1.8e6]);
    ylim([0,5]);
end
