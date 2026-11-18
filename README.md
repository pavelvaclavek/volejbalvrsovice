# Volejbal Vršovice – evidence příspěvků a čerpání

Aplikace pro Windows, ve které oddíl volejbalu TJ Sokol Praha Vršovice vede družstva, hráče, jejich příspěvky a náklady na akce – a z toho saldo každého hráče. Neinstaluje se a funguje bez internetu; data zůstávají v zašifrované databázi ve složce aplikace.

## Stažení

**[VolejbalVrsovice.zip – poslední verze](https://github.com/pavelvaclavek/volejbalvrsovice/releases/latest/download/VolejbalVrsovice.zip)**

Odkaz je trvalý a vždy vede na poslední verzi. Přehled verzí s kontrolními součty je na stránce [Vydání](https://github.com/pavelvaclavek/volejbalvrsovice/releases).

> **Bez hesla i záchranného kódu jsou data nenávratně ztracena.** Záchranný kód, který aplikace ukáže po prvním přihlášení, si uložte mimo počítač.

## Jak začít

Potřebujete Windows 10 (22H2 nebo novější) nebo Windows 11, 64bitové. Nic dalšího se neinstaluje.

1. **Odblokujte stažený ZIP** ještě před rozbalením: pravým tlačítkem → *Vlastnosti* → dole zaškrtněte *Odblokovat* → *OK*.
2. Rozbalte ho do složky, kam smíte zapisovat, například `C:\VolejbalVrsovice` – ne do *Program Files*.
3. Spusťte `VolejbalVrsovice.exe`. Ozve-li se **SmartScreen** („Systém Windows ochránil váš počítač“), klepněte na *Další informace* a potom na *Přesto spustit* – aplikace nemá digitální podpis.
4. Přihlaste se výchozím účtem: e-mail `admin@admin.cz`, heslo `admin`. Aplikace si hned vynutí vlastní heslo a potom jednou ukáže **záchranný kód** – uložte ho na bezpečné místo.

Podrobný návod je v souboru `NAVOD.txt` uvnitř ZIPu.

## Na co si dát pozor

- **Žádný OneDrive, Disk Google ani Dropbox.** Složku s aplikací mějte na místním disku nebo ve sdílené síťové složce. Přes cloudovou synchronizaci nefunguje zámek proti souběžnému otevření a vznikají konfliktní kopie databáze. Pozor na složky *Plocha* a *Dokumenty*, které bývají do OneDrive zapojené také.
- **Jen jeden člověk najednou.** Dalšímu aplikace oznámí, kdo ji právě používá, a nespustí se.
- **Zálohujte** soubory `Data\volejbal.db` a `Data\keys.json` – vždy oba společně, nejjednodušeji celou složku `Data`.
- **Deaktivovaný uživatel**, který si dřív zkopíroval celou složku aplikace, tuto starou kopii svým tehdejším heslem dál otevře. Hlídejte proto, kdo má ke složce a k zálohám přístup.

## Aktualizace

Když vyjde novější verze, aplikace to po přihlášení oznámí proužkem s odkazem ke stažení. Nic se neinstaluje samo: stáhněte ZIP z téhož odkazu, odblokujte ho, zavřete aplikaci a rozbalte ho přes stávající složku. Data zůstanou – ZIP složku `Data` neobsahuje.

---

V tomto repozitáři jsou jen vydané verze aplikace.
