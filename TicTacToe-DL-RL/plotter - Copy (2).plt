set grid
set terminal wxt 0
plot ".\\bin\\Release3\\plotdata.txt" using 2 with lines title "Winrate Player X (starts) [against self]",\
'' using 3 with lines title "Winrate Player Z [against self]",\
'' using 4 with lines title "Drawrate [against self]",\
'' using 6 with lines title "Winrate [against random]"

set terminal wxt 1
plot ".\\bin\\Release3\\plotdata.txt" using 1 with lines title "Pseudo ELO"

set terminal wxt 2
plot ".\\bin\\Release3\\plotdata.txt" using 5 with lines title "Avg. Game Length"
pause 20
reread