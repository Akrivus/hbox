# Roman Annals prototype

Roman Annals is a separate HBOx channel for short, scripted dramatizations of Roman history. Its fixture starts in the Late Republic, in 63 BCE. The year, period, room, and episode ID are stored with the script so later episodes can be organized chronologically and refer back to earlier events.

## Try the pilot

1. Open `Assets/Main.unity` and enter Play Mode. If `hbox.json` has a channel allowlist, add `romanannals` to it before starting.
2. Select **Roman Annals** in the guide. The Catiline debate starts after the channel transition, with offline scratch voices and captions. Select with the guide closed to pause or resume. To replay, switch away and back.
3. Check the current-speaker border, corner names, subtitles and decision card. Switch away during a line and confirm that the old audio stops. Try both 16:9 and 9:16 Game view sizes.

The pilot reuses the Polbots `VideoScreenComponent` prefab with static bust textures. HBOx loads it through the standard guide and scene registration. A scene-local player runs the authored fixture with the bundled clips, independent of chat generation, chat replay and automatic OBS recording.

The fixture lives at `Assets/Scenes/RomanAnnals/Fixture/Catiline.json`. The scene's portrait references follow the cast order. Its audio references follow the line order. The scratch tracks were generated with FFmpeg's FLite filter using voices `rms` (Cicero), `kal16` (Caesar), and `awb` (Cato). Re-record or regenerate a track when editing its line.

## Historical and portrait sources

The pilot condenses [Sallust, *The War with Catiline*, 50–55](https://penelope.uchicago.edu/Thayer/E/Roman/Texts/Sallust/Bellum_Catilinae*.html#50) into a single call. The English dialogue is original dramatization. Caesar's argument about precedents and Cato's appeal to ancestral practice are drawn from Sallust's accounts of their speeches.

The three portrait files are resized and cropped adaptations of these photographs.

| Asset | Source and author | License for the photograph and adaptation |
| --- | --- | --- |
| `Cicero.jpg` | [Bust of Cicero](https://commons.wikimedia.org/wiki/File:Bust_of_Cicero.jpg), Joseolgon | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |
| `Caesar.jpg` | [Retrato de Julio César](https://commons.wikimedia.org/wiki/File:Retrato_de_Julio_C%C3%A9sar_(26724093101).jpg), Ángel M. Felicísimo | [CC BY 2.0](https://creativecommons.org/licenses/by/2.0/) |
| `Cato.jpg` | [Cato Volubilis bronze bust](https://commons.wikimedia.org/wiki/File:Cato_Volubilis_bronze_bust.jpg), Prioryman | [CC BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/) |
