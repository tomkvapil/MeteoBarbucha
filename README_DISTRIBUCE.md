# Jak publikovat MeteoBarbucha ČR online a zapnout automatické aktualizace

Aplikace **MeteoBarbucha.exe** je samostatný přenositelný soubor (portable) s vestavěným aktualizačním modulem (`UpdateService`).

## Doporučený postup zdarma (GitHub Releases)

1. Vytvořte si na [GitHub.com](https://github.com) veřejný repozitář (např. `MeteoBarbucha`).
2. Vytvořte nový **Release** (např. tag `v1.0.0`) a nahrajte do něj soubor **`MeteoBarbucha.exe`**.
3. Do aplikace (tlačítko **Aktualizace** v horní liště) vložte URL:
   ```text
   https://api.github.com/repos/VAS_UCET/MeteoBarbucha/releases/latest
   ```
4. Aplikace si tuto adresu zapamatuje a při každém spuštění (nebo po kliknutí na **Aktualizace**) zkontroluje nejnovější Release.
5. Kdykoliv nahrajete nový Release (např. `v1.0.1`) s novým `MeteoBarbucha.exe` (a zvýšíte `UpdateService.CurrentVersion` v kódu), aplikace sama nabídne stažení, nahradí svůj `.exe` soubor a automaticky se restartuje.

## Alternativa: Vlastní webhosting

Nahrajte na svůj web vedle sebe soubory `MeteoBarbucha.exe` a `version.json` (šablona je v této složce) a v aplikaci zadejte URL na `version.json`.
