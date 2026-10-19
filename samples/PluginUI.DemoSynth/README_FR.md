<!-- 2026/10/19 07:48:00 UTC - PluginUI -->

# PluginUI DemoSynth

[English version](README.md)

Un petit instrument VST3 complet, écrit en C# avec [NPlug](https://github.com/xoofx/NPlug) et PluginUI. Il montre ce qu'un instrument demande de plus qu'un effet :

- une **entrée d'événements MIDI**, avec des notes placées à l'échantillon près ;
- un **clavier à l'écran** joué jusque dans le processeur ;
- le **pitch bend et la molette de modulation** associés à des paramètres (`IMidiMapping`) ;
- le **MIDI learn** depuis le menu contextuel de chaque contrôle ;
- une **entrée audio sur un instrument** : un sidechain stéréo optionnel qui fait baisser le synthé (effet de pompage) ;
- un **retour en temps réel** depuis le thread audio : notes tenues, vumètres de sortie et de sidechain, courbe de réduction de gain ;
- une **comparaison A/B** de tout le son.

![Éditeur de DemoSynth](../../docs/images/13-demosynth.png)

## Moteur sonore

16 voix. Chaque voix comprend :

- deux oscillateurs PolyBLEP désaccordés (de la dent de scie au carré) ;
- un passe-bas résonant à rétroaction sans retard, avec sa propre ADSR ;
- une ADSR d'amplitude.

La molette de modulation commande un vibrato (5,5 Hz, jusqu'à ±50 cents) et le pitch bend couvre ±2 demi-tons.

Le vol de voix réutilise d'abord la même note, puis une voix libre, puis la voix en relâchement la plus faible, puis la plus ancienne. Le moteur (`Dsp/`) n'alloue rien après sa construction.

| Paramètre | Plage | Défaut |
|---|---|---|
| Volume | −48 … +6 dB | −9 dB |
| Shape | 0 % (dent de scie) … 100 % (carré) | 0 % |
| Detune | 0 … 50 cents | 8 cents |
| Cutoff | 20 Hz … 20 kHz (log.) | 2,4 kHz |
| Resonance | 0 … 100 % | 20 % |
| Filter Env | −5 … +5 octaves | +2 octaves |
| Amp / Filter Attack | 1 ms … 5 s (log.) | 5 ms / 2 ms |
| Amp / Filter Decay | 5 ms … 10 s (log.) | 300 ms / 400 ms |
| Amp / Filter Sustain | 0 … 100 % | 70 % / 20 % |
| Amp / Filter Release | 5 ms … 10 s (log.) | 350 ms / 400 ms |
| Pitch Bend | ±2 demi-tons (pitch bend MIDI) | 0 |
| Mod Wheel | 0 … 100 % (CC 1) | 0 % |
| Sidechain Duck | 0 … 100 % | 0 % |
| Duck Release | 20 ms … 1 s (log.) | 150 ms |

Bus :

- entrée d'événements « MIDI In » (16 canaux) ;
- sortie audio « Output » (stéréo) ;
- entrée audio « Sidechain » (stéréo, auxiliaire), inactive tant qu'aucune piste n'y est envoyée.

## Organisation

| Fichier | Rôle |
|---|---|
| `Plugin/DemoSynthModel.cs` | Paramètres, conversion en `SynthSettings` |
| `Plugin/DemoSynthProcessor.cs` | `PluginUIProcessor<T>` : bus, événements à l'échantillon près, notes de l'éditeur, sidechain, vumètres |
| `Plugin/DemoSynthController.cs` | `PluginUIController<T>` : association pitch bend et molette, MIDI learn activé, éditeur |
| `Plugin/DemoSynthShared.cs` | Données sans verrou partagées par le processeur avec l'éditeur |
| `Plugin/DemoSynthPlugin.cs` | Fabrique (catégorie Instrument/Synth) |
| `Dsp/SynthDsp.cs` | Oscillateur PolyBLEP, ADSR, filtre à variables d'état ZDF, suiveur d'enveloppe |
| `Dsp/SynthEngine.cs` | Voix, allocation, rendu, atténuation par sidechain |
| `Editor/DemoSynthEditor.cs` | Éditeur PluginUI |

L'éditeur envoie les touches jouées sur son clavier avec `ConnectKeyboard`, et le processeur les lit dans `ProcessMain` avec `TryDequeueUINote`. L'objet renvoyé par `CreateSharedState` parvient au contrôleur quand les deux composants se connectent ; l'éditeur y branche ses vumètres et son clavier.

## Compiler, déboguer, installer

```
dotnet build -c Debug                      # bin\Debug\net10.0\win-x64\PluginUI.DemoSynth.vst3 (proxy CoreCLR, débogable)
dotnet publish -c Release -r win-x64       # NativeAOT : PluginUI.DemoSynth.vst3, version AVX2 (x86-64-v3)
dotnet publish -c Release -r win-x64 -p:IlcInstructionSet=x86-64-v2   # pour les processeurs sans AVX2
```

Copiez le `.vst3` dans `C:\Program Files\Common Files\VST3`, puis relancez la détection des plugins dans le DAW. Le projet a besoin des sources de NPlug : le sous-module `external/NPlug` du dépôt, un clone voisin `..\NPlug`, ou `NPlugRoot`. Des bundles prêts à l'emploi (x64 et ARM64) sont joints à chaque [version](https://github.com/asm8086/PluginUI/releases).

Routage du sidechain :

- Cubase : ouvrez le sidechain de l'instrument depuis l'en-tête de la fenêtre du plugin ;
- Reaper : passez la piste en 4 canaux et envoyez la grosse caisse sur les canaux 3/4 ;
- Bitwig : choisissez la source dans le sélecteur de sidechain du périphérique.

MIDI learn : faites un clic droit sur un contrôle, choisissez **MIDI Learn**, puis tournez un bouton de votre contrôleur. L'apprentissage demande un hôte qui signale les contrôleurs MIDI reçus en direct (`IMidiLearn` de VST3, par exemple Cubase et Nuendo) ; ailleurs, utilisez le MIDI learn de l'hôte. Les affectations sont enregistrées avec le projet.

## Tests

`tests/PluginUI.NPlug.Tests` :

- compare les enveloppes, le filtre et les oscillateurs à leurs formules exactes ;
- vérifie le moteur : hauteur, relâchement, vol de voix, identifiants de note, atténuation par sidechain, réglages extrêmes ;
- passe le validateur officiel de Steinberg sur le plugin (47 tests réussis).
