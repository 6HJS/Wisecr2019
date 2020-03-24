clear;
close all;
clc;
x = 1:8;
x2 = 1:0.1:8;
d10 = [3.69 3.71 3.71 3.68 3.69 3.71 3.69 3.69 3.71 3.71]';
d20 = [2.82 2.75 2.90 2.84 2.89 2.89 2.85 2.85 2.76 2.81]';
d30 = [2.24 2.35 2.37 2.30 2.30 2.30 2.40 2.30 2.38 2.35]';
d40 = [2.18 2.18 2.17 2.18 2.04 2.07 2.18 2.08 2.08 2.18]';
d50 = [2.04 2.15 2.15 2.06 2.15 2.15 2.16 2.15 2.15 2.16]';
d60 = [0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00]';
d70 = [0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00]';
d80 = [0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00 0.00]';
mean80 = [mean(d10),mean(d20),mean(d30),mean(d40),mean(d50),mean(d60),mean(d70),mean(d80)];

figure('Renderer', 'painters', 'Position', [10 1200 1000 300]);
hold on;
plot(mean80,'r');
boxplot([d10,d20,d30,d40,d50,d60,d70,d80],'Notch','on','Labels',{'10','20','30 ','40 ','50 ','60 ','70 ','80 ',},'Whisker',1);
set(findall(gcf,'-property','FontSize'),'FontSize',18);
xlabel('Operational Range {\it d} (cm)','FontSize',20);
ylabel('Token Voltage {\it V_t} (V)');

print(gcf, 'TeTo30ms.pdf', '-dpdf', '-bestfit')


%% V_t to Tsleep and Tactive
C = 52.7e-6; %capacitance
Vf = 3.83; %final volatage
Vi = 2.14; %initial voltage
Vbo = 1.8; %brounout voltage
t = 30e-3; %test touch time
I2 = 240e-6; % active current

zeta_d = t./(C*log((Vf-Vi)./(Vf-mean80)));
Ff = C*log((Vf-Vi)/(Vf-0.98*Vf));%factor final
t_fc = zeta_d .* Ff;
t_fc(t_fc <0 ) = 0; %zero out negative value

t_bo = zeros(1,length(t_fc));
t_bo(t_fc ~= 0) = (0.98*Vf-Vbo)*C/I2;

t = [t_fc;t_bo]';
figure('Renderer', 'painters', 'Position', [10 1200 1000 300]);
bar(t);
set(gca,'xticklabel',{'10','20','30','40','50','60','70','80',});
set(findall(gcf,'-property','FontSize'),'FontSize',18);
xlabel('Operational Range {\it d} (cm)','FontSize',20);
ylabel('Time {\it t} (s)');
print(gcf, 'TeToTslTac.pdf', '-dpdf', '-bestfit')

hold off;
figure();
Tfc10 = [37.4	37.4	37.4	37.4	37.4	37.4	37.4	37.4	37.4	37.4]/1000;
Vfc10 = [2.83	2.89	2.88	3.37	3.39	3.10	3.44	3.10	3.46	3.21];
Tbo10 = [466.4	466.4	466.4	466.4	466.4	466.4	466.4	466.4	466.4	466.4]/1000;
Vbo10 = [3.23	3.39	3.18	3.16	3.19	3.17	3.2	3.2	3.18	3.2];

Tfc20 = [185	186	186	186	186	186	186	186	186	186]/1000;
Vfc20 = [2.93	2.94	2.93	2.94	2.74	2.71	2.93	2.94	2.93	2.92];
Tbo20 = [614	615	615	615	615	615	615	615	615	615]/1000;
Vbo20 = [2.87	2.78	2.84	2.82	2.59	2.56	2.81	2.83	2.81	2.79];

Tfc30 = [841	841	841	841	841	841	841	841	841	841]/1000;
Vfc30 = [2.36	2.33	2.31	2.3	2.5	2.45	2.43	2.45	2.44	2.73];
Tbo30 = [1270	1270	1270	1270	1270	1270	1270	1270	1270	1270]/1000;
Vbo30 = [2.13	2.1	2.11	2.11	2.32	2.26	2.28	2.26	2.28	2.51];

for pidx = 1:3
    subplot(3,1,pidx);
    t = 0:1e-3:2;
    t_fc(pidx); %time to fully charged
    if (zeta_d(pidx) > 0)
        Vct = Vi+(Vf-Vi)*(1-exp(-t/(zeta_d(pidx)*C)));
    else
        Vct = zeros(1,2001);
    end
    %================================
    %Q=VC, dV = dQ/C = dt*I2/C
    
    if(t_fc(pidx) == 0)
        break;
    end
    for tidx = 1:2000
        if t(tidx) >= t_fc(pidx) + t_bo(pidx)
            Vct(tidx) = Vct(tidx-1);
            continue;
        end
        if(t(tidx) >= t_fc(pidx))
            Vct(tidx) = Vct(tidx-1) - (1e-3 * I2 / C);
        end
    end
    
    h = plot(t,Vct);
    xlim = ([0 2]);
    ylim = ([0 4]);
    set(findall(gcf,'-property','FontSize'),'FontSize',18);
    if(pidx == 3)
        xlabel('Time {\it t} (s)','FontSize',20);
    end
    if(pidx == 2)
        ylabel('Voltage {\it V_C(t)} (s)');
    end
    hold on;
    if(pidx == 1)
        scatter(Tfc10,Vfc10,'bo');
        scatter(Tbo10,Vbo10,'rx');
    end
    if(pidx == 2)
        scatter(Tfc20,Vfc20,'bo');
        scatter(Tbo20,Vbo20,'rx');
    end
    if(pidx == 3)
        scatter(Tfc30,Vfc30,'bo');
        scatter(Tbo30,Vbo30,'rx');
    end


end