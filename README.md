# NEON // PC Monitor

Un widget nativo per Windows 10/11 a 64 bit: sfondo trasparente, pannello rettangolare verticale con angoli arrotondati, caratteri da terminale e grafici essenziali. Il pannello occupa al massimo metà dell'altezza dell'area desktop disponibile sul monitor e riduce la larghezza in proporzione, considerando la scala DPI di Windows. L'interfaccia ha spazi ridotti e conserva CPU, RAM, GPU, temperature, rete e unità nella vista completa. Vive sul desktop e mostra lo stato del computer mentre lavori o giochi.

## Avvio

Fai doppio clic su **Avvia.cmd**, oppure apri **bin/NeonMonitor.exe** dopo la compilazione. Non serve installare Python, Node o un SDK. Usa .NET Framework 4.x e WPF, inclusi nelle normali installazioni di Windows 10/11.

Il programma non richiede privilegi amministrativi. Un secondo avvio riapre il widget esistente.

## Cosa fa la prima versione

| Area | Dati |
| --- | --- |
| CPU | Utilizzo totale, modello e numero di thread logici |
| RAM | Percentuale, memoria usata/totale e memoria impegnata rispetto al limite di commit |
| GPU | Utilizzo, modello e VRAM quando esposti dal provider |
| Temperature | Massimo dei sensori CPU e GPU disponibili; aggiornamento ogni 5 secondi |
| Rete | Download e upload aggregati sulle interfacce attive, in B/s, KiB/s e MiB/s |
| Dischi | Tutte le unità locali fisse (sul PC attuale C: ed E:), ognuna con spazio occupato, capacità e barra percentuale; SYS identifica l'unità di sistema |
| Sessione | Orologio e tempo dall'avvio del kernel Windows |

- Letture di sistema circa ogni secondo e grafici degli ultimi 60 campioni. I campioni rimangono solo in memoria; nessuna cronologia è salvata su disco.
- Spostamento trascinando la scritta `NEON / SYS`.
- Pulsante `P` per rimanere sopra le altre finestre, `=` per il pannello compatto.
- La dimensione si adatta al monitor: anche la vista completa resta entro metà altezza desktop. I modelli di CPU e GPU si leggono passando il mouse sui rispettivi riquadri.
- Menu `···` o tasto destro: menu scuro con simboli, spunte e sottomenu. In `Sfondo` scegli la trasparenza completa oppure il 35%, 60% o 85% di opacità. Testo e linee mantengono la loro leggibilità. Lo stesso menu è disponibile con il tasto destro sull'icona nell'area di notifica.
- Nel menu `··· → Template colori` trovi **Matrix** (verde), **Ice** (azzurro), **Cyberpunk** (viola e ciano), **Amber** (arancio), **Crimson** (rosso) e **Ghost** (bianco). Cambiano testo, grafici, bordi e sfondi; le segnalazioni conservano un colore di attenzione. La scelta è salvata e viene ripristinata al prossimo avvio.
- `−` nasconde il pannello. Doppio clic sull'icona nell'area di notifica per mostrarlo. `×` chiude l'app.
- L'icona NEON è una N verde su fondo scuro, presente nell'eseguibile, nella finestra e nell'area di notifica. La versione ICO contiene otto dimensioni da 16 a 256 pixel; il sorgente vettoriale è `assets/neon.svg` e la generazione è riproducibile con `assets/build-icon.ps1`.
- Posizione, trasparenza, tema e modalità salvati in `data/settings.json` nella cartella del progetto.
- Indicatori visivi per temperature alte (CPU da 90 °C, GPU da 85 °C), CPU/RAM dal 90%, disco dal 95%. Sono soglie indicative fisse, non diagnosi sullo stato dell'hardware.
- Nessun account, telemetria remota o collegamento a servizi cloud. L'app non modifica clock, ventole o impostazioni hardware.

Il riquadro dischi elenca i volumi con lettera di unità, non i dispositivi fisici: un dispositivo partizionato può comparire più volte. Le unità non accessibili restano visibili come N/D; quelle rimosse spariscono alla lettura successiva. Se l'elenco è lungo, puoi scorrerlo. Supporti rimovibili e unità di rete non sono inclusi.

## Temperature e sensori GPU

Windows non offre una temperatura CPU universale. **N/D** indica un valore non disponibile, non una temperatura pari a zero.

1. Per le GPU NVIDIA, NEON cerca `nvidia-smi.exe` nel driver e legge carico, temperatura e VRAM della prima GPU. Non occorre un altro programma se il driver espone queste informazioni.
2. Per CPU e altre GPU, puoi usare [Libre Hardware Monitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), avviato sul PC con il provider WMI disponibile. Alcuni sensori richiedono di avviare **Libre Hardware Monitor** come amministratore. NEON non lo installa né lo avvia automaticamente.
3. È supportato anche il provider WMI di Open Hardware Monitor. Quando il provider scompare, i valori non più disponibili tornano a N/D.

Le letture WMI e NVIDIA avvengono in background. L'app rimane utilizzabile durante le interrogazioni. Il valore termico mostrato è il massimo dei sensori del dispositivo, che può includere core, package o hotspot a seconda del provider. Le metriche GPU provengono dallo stesso dispositivo; la prima GPU esposta dal provider è quella monitorata. Nessuna stima basata sul carico.

I contatori di rete sommano le interfacce attive: con VPN o adattatori virtuali alcuni pacchetti possono essere conteggiati su più interfacce. L'uptime può comprendere precedenti sessioni se Windows usa Avvio rapido. Non ci sono notifiche sonore o popup periodici.

## Sviluppo e verifica

```powershell
.\build.ps1
.\tests\verify.ps1
```

Il compilatore usato è quello di .NET Framework già presente in Windows. Il codice è C# con UI WPF; nessun pacchetto esterno da scaricare.

Le verifiche coprono il calcolo CPU, le velocità di rete con intervalli effettivi, reset dei contatori, aggiunta/rimozione di adattatori e letture reali di tutte le unità fisse. La verifica UI controlla modalità compatta, pin, trasparenza, allarmi, sensori mancanti, unità multiple/non accessibili/rimosse e tutti i temi; genera anche `artifacts/neon-preview.png` e la galleria `artifacts/neon-themes.png` con dati reali. La verifica UI usa campioni sintetici solo per esercitare gli stati di errore, mai nella modalità normale o nelle anteprime.

## Evoluzione proposta

La base è un monitor discreto e leggibile, con attenzione al consumo di risorse. Per una versione successiva: integrazione diretta dei sensori senza provider esterno, scelta della GPU e filtro dei dischi, dimensione personalizzabile, soglie personalizzabili e avvio con Windows attivabile dalle preferenze. L'avvio automatico non viene configurato da questa versione.
