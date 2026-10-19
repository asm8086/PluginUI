<!-- 2026/10/19 07:48:00 UTC - PluginUI -->

# Contribuer à PluginUI

*English version: [CONTRIBUTING.md](CONTRIBUTING.md)*

Merci de votre intérêt ! Les signalements de bogues, questions, idées et demandes de fusion (pull requests) sont les
bienvenus.

## Signaler un problème

Ouvrez un [ticket](https://github.com/asm8086/PluginUI/issues) avec :

- ce que vous avez fait, ce que vous attendiez et ce qui s'est passé (une capture d'écran aide pour un problème de
  dessin) ;
- la version de Windows, la mise à l'échelle (DPI), le DAW et sa version pour un problème de plugin ;
- le plus petit code ou design `.pluginui` qui montre le problème, si possible.

## Obtenir les sources

```
git clone --recurse-submodules https://github.com/asm8086/PluginUI.git
```

`external/NPlug` est un sous-module : [xoofx/NPlug](https://github.com/xoofx/NPlug) à la version avec laquelle
PluginUI est compilé et testé (étiquette `0.6.0`). Si vous avez cloné sans `--recurse-submodules`, lancez
`git submodule update --init --recursive`.

## Compiler et tester

Prérequis : Windows 10 1809 ou ultérieur, le SDK .NET 10, Visual Studio 2026 (avec la charge de travail
*Développement Desktop en C++* pour publier en NativeAOT). Les bibliothèques et les tests compilent et s'exécutent
aussi sous Linux et macOS (`EnableWindowsTargeting`), ce que fait une partie de l'intégration continue.

```
dotnet build PluginUI.slnx -c Release
dotnet test PluginUI.slnx -c Release
```

Autres commandes utiles :

| Commande | Rôle |
|---|---|
| `dotnet run --project samples/PluginUI.Gallery` | Tous les éléments dans une fenêtre (`-- --lang fr` en français) |
| `dotnet run --project tools/PluginUIEditor` | Le concepteur visuel |
| `dotnet tool restore` puis `dotnet docfx docfx/docfx.json --serve` | Le site de documentation |
| `pwsh build/Build-Release.ps1` | Les paquets de version, comme les produit le workflow de publication (Windows) |

## Conventions

- **Langue** : identifiants, commentaires et documentation XML en anglais. Les guides existent en anglais et en
  français (`X.md` et `X_FR.md`) : mettez les deux à jour, ou indiquez dans la demande de fusion lequel reste à
  traduire.
- **Entête des fichiers** : chaque fichier source commence par un commentaire donnant la date de sa dernière révision
  (`yyyy/MM/dd HH:mm:ss UTC`) et le nom du projet, suivi des lignes SPDX :

  ```csharp
  // 2026/10/18 16:20:00 UTC - PluginUI
  // SPDX-FileCopyrightText: 2026 asm8086
  // SPDX-License-Identifier: MIT
  ```

- **Aucune dépendance à la culture** : les nombres sont formatés et lus avec la culture invariante (CA1304 / CA1305
  sont des erreurs). Une virgule décimale n'est jamais affichée ni acceptée.
- **NativeAOT** : les bibliothèques compilent avec les analyseurs AOT et trimming, avertissements traités comme des
  erreurs. Pas de réflexion invisible pour le trimmer, pas de sérialiseur `System.Text.Json` par réflexion.
- **API publique** : chaque membre public a sa documentation XML, écrite pour ses utilisateurs.
- **Temps réel** : le code appelé depuis le thread audio n'alloue rien, ne prend aucun verrou et ne bloque pas.
- **Contrôles** : suivez les conventions de conception d'[EXTENDING_FR.md](docs/EXTENDING_FR.md#13-conventions-de-conception-pluginuieditor)
  pour qu'ils fonctionnent dans le concepteur visuel.
- **Tests** : ajoutez ou mettez à jour les tests de `tests/` avec la modification. Les tests n'ouvrent aucune fenêtre ;
  ils pilotent l'hôte par `UIHost.Dispatch…`.
- **Captures** : quand une modification se voit, régénérez les captures de la galerie (voir le README, section
  *Galerie*).

## Modifications de NPlug

Les corrections de NPlug vont dans son dépôt ([xoofx/NPlug](https://github.com/xoofx/NPlug)). Une fois publiées, le
sous-module passe à la nouvelle étiquette (`git -C external/NPlug checkout <étiquette>`, puis validez le sous-module).

## Demandes de fusion

1. Forkez le dépôt et créez une branche à partir de `main`.
2. Gardez la modification ciblée ; décrivez ce qu'elle fait et comment vous l'avez testée.
3. Vérifiez que `dotnet build` (sans avertissement) et `dotnet test` passent ; l'intégration continue les lance sous
   Linux et Windows.
4. Ajoutez une ligne dans la section *Unreleased* de [CHANGELOG.md](CHANGELOG.md) pour un changement visible des
   utilisateurs.

En contribuant, vous acceptez que vos contributions soient publiées sous [licence MIT](LICENSE).

## Publication des versions

Les mainteneurs publient une version en poussant une étiquette `vX.Y.Z`. Le workflow de publication compile et teste
tout, crée le bundle VST3 de l'instrument de démonstration pour x64 (x86-64-v3 et x86-64-v2) et ARM64, les paquets de
PluginUIEditor et de la galerie, pousse les paquets NuGet (quand la variable de dépôt `PUBLISH_NUGET` vaut `true` et que la publication de confiance est configurée sur nuget.org) et publie la version GitHub.
