> [English version](../en/architecture.md)

# Architecture de l'app IRC

## But

IRC est une app fenêtrée de Kazo OS : un client de discussion natif, en C# et
Blazor, pour les vrais réseaux IRC. Le but et les décisions sont dans le
[plan](plan.md). Ce document décrit le code ; chaque chantier de la série
`irc/base` le complète (le protocole au chantier 2, la connexion au chantier 3,
etc.).

## Solution

```text
Os-App-IRC/
├─ external/KazoOs/          # sous-module : Kazo OS sur le tag v3.0.0 (la version du dépôt)
├─ Directory.Build.props     # racine du sous-module, profil, import de Versions.props
├─ Directory.Build.targets   # mode source en Debug, KOSAPP0070 sans sous-module
├─ global.json
├─ Kazo.Irc.slnx
├─ src/
│  ├─ Kazo.Irc/              # l'app
│  └─ Kazo.Irc.Os/           # hôte : Kazo OS, ses apps de base et IRC
├─ tests/
│  ├─ Kazo.Irc.Tests/        # tests unitaires C# (xUnit)
│  └─ e2e/                   # parcours dans l'hôte (node --test + Playwright)
├─ .github/workflows/        # ci.yml, publish-*.yml, tag-release.yml
└─ package.json              # outils des tests de bout en bout
```

Le dépôt suit la sortie du modèle **`kazoos-solution`** de Kazo OS 3.0.0
(ADR 0176 et 0207), comme [Chess](https://github.com/Kazo-ca/Os-App-Chess) et
[Webamp](https://github.com/Kazo-ca/Os-App-Webamp) : `external/KazoOs` est
Kazo OS en sous-module, exactement sur un tag de release, et son
`build/Versions.props` donne **la** version du dépôt. On ne modifie jamais le
sous-module : la CI refuse un sous-module modifié ou hors de son tag
(`.github/scripts/check-kazoos-submodule.sh`). Les workflows viennent de Webamp
(CI avec les tests Playwright, « Tag a release », publication des paquets
**sans signature** — la signature se fait seulement dans Os-AppMarketPlace,
ADR 0219 de Kazo OS — et proposition au catalogue du Kazo OS Store).

| Profil | Kazo OS vient de | Usage |
| --- | --- | --- |
| `Debug` (`KazoOsUseSource=true`) | les projets du sous-module (on entre dans le SDK et l'OS) | développement, débogage |
| `Release` (`KazoOsUseSource=false`) | les paquets `Kazo.Os.*` de nuget.org, à la version du sous-module | CI, tests E2E, Kazo OS Store |

Cloner : `git clone --recurse-submodules`, ou `git submodule update --init`
après coup (sans sous-module, le build s'arrête sur `KOSAPP0070`).

**Hôte.** `Kazo.Irc.Os` est un Kazo OS complet avec les apps de base et IRC en
`ProjectReference`. `dotnet run --project src/Kazo.Irc.Os` le lance sur
<http://localhost:5310>.

## L'app `Kazo.Irc`

| Dossier ou fichier | Rôle |
| --- | --- |
| `IrcApp.cs` | Manifeste (`[AppManifest]`) : id `ca.kazo.irc`, nom « IRC », version `1.0.0`, éditeur `pub.kazo`, catégorie `communication` (décision 20), **ordinateur et mobile** (`MobileEntryPoint` = la même fenêtre, décision 7), `SingleInstancePerUser` (décision 8). Fenêtre de 960×620, redimensionnable jusqu'à 340×400. Capacités du chantier 1 : `filesystem.private.read` et `.write` seulement (le fichier de réglages). Chaque chantier ajoute celles que son code utilise : `network.real.access` et les ports réels au chantier 3 (après l'ADR 0235 §3 de Kazo OS), les notifications et les journaux au chantier 4, `apps.launch` au chantier 5. |
| `IrcWindow.razor`, `.razor.cs`, `.razor.css` | La fenêtre unique : barre d'outils, trois colonnes, panneaux du mode compact, ligne d'état. |
| `Components/ChannelList` | Colonne de gauche : les réseaux et leurs salons (vide au chantier 1). |
| `Components/MessagePane` | Colonne du milieu : les messages (`role="log"`) et la ligne de saisie (désactivée tant qu'il n'y a pas de connexion). |
| `Components/UserList` | Colonne de droite : les utilisateurs du salon (vide au chantier 1). |
| `Layout/IrcLayout.cs`, `IrcPanel.cs` | Quel panneau latéral est ouvert en mode compact (aucun, salons ou utilisateurs ; un seul à la fois). |
| `Settings/IrcSettings.cs` | `~/.config/ca.kazo.irc/settings.json` : la police console (décision 19). Un fichier absent, illisible ou cassé donne les valeurs par défaut ; un champ inconnu (ajouté par un chantier suivant) est ignoré. |
| `Settings/IIrcFileSystem.cs`, `KazoIrcFileSystem.cs` | Lecture et écriture des fichiers de l'app ; une version en mémoire sert aux tests. |
| `Resources/Strings*.resx` | Les textes de l'interface, en anglais et en français (ADR 0104). |
| `Resources/AppManifest.fr.resx` | Nom et description en français, écrits dans `localization/fr.json` par le build. |
| `wwwroot/icons/app.svg` | Icône illustrée dans le style des icônes d'apps (ADR 0232 de Kazo OS) : tuile sombre commune, deux bulles de discussion, un `#` dans celle de devant. |

### Mise en page

La fenêtre décide de sa mise en page d'après **sa propre largeur** (requête de
conteneur CSS sur `.irc-window`), pas d'après l'écran : une fenêtre étroite sur
ordinateur se comporte comme un téléphone.

| Largeur de la fenêtre | Mise en page |
| --- | --- |
| 720 px et plus (`IrcApp.CompactWidth`) | Trois colonnes : réseaux et salons (220 px), messages, utilisateurs (220 px). |
| Moins de 720 px | Une colonne : les messages. Les boutons « Salons » et « Utilisateurs » de la barre d'outils ouvrent un panneau qui glisse par-dessus, à gauche ou à droite ; un fond sombre, le bouton × du panneau ou Échap le ferment. Ouvrir l'un ferme l'autre. |

Dans le shell mobile, une bande transparente de 1,75 rem en haut de chaque app
appartient au volet de notifications (`.mobile-shade-handle`) : la barre
d'outils commence sous elle (`.mobile-shell .irc-toolbar`), sinon ses boutons ne
reçoivent pas les touchers.

### Thème et police

Toutes les couleurs viennent des variables du thème de l'OS (`--kos-*`), avec
une valeur de repli. La variable `--irc-text-font` donne la police des messages,
des listes et de la saisie : la police d'interface du thème
(`--kos-font-ui`), ou sa police à chasse fixe (`--kos-font-mono`) quand le
bouton « Police console » est enfoncé (`aria-pressed`). Le choix est enregistré
tout de suite ; si l'écriture échoue, la police change quand même et la ligne
d'état dit pourquoi le choix ne sera pas gardé.

### Le dossier de l'app

`KazoDirectory.CreateDirectoryAsync` regarde chaque dossier parent avant de
créer (comme `mkdir -p`) ; regarder `~/.config` demande
`filesystem.user-home.read`, que l'app ne demande pas. `KazoIrcFileSystem` crée
donc seulement `~/.config/ca.kazo.irc/` (comme `mkdir` sans `-p`), par la
passerelle de fichiers de l'app, quand il n'existe pas.

## Tests

| Suite | Commande | Contenu |
| --- | --- | --- |
| C# (xUnit) | `dotnet test Kazo.Irc.slnx -c Release` | manifeste (classe et `manifest.json` produit, textes français, icône), textes anglais et français complets, panneaux du mode compact, réglages (chemin, défauts, fichier cassé ou refusé, champ inconnu, écriture qui échoue). |
| Bout en bout | `npm run test:e2e` après `dotnet build Kazo.Irc.slnx -c Release` | dans l'hôte, profil neuf : trois colonnes côte à côte sur ordinateur dans le thème de l'OS ; police console gardée après fermeture et réouverture ; sur un Pixel 7, une colonne et les deux panneaux. |

Les tests de bout en bout démarrent l'hôte (`dotnet run --no-build -c Release`)
sur un port libre, ou utilisent `IRC_BASE_URL`.

## Avancement

| Chantier | Ce qu'il a ajouté ici |
| --- | --- |
| 1. `irc/1-solution` | Solution, hôte, CI, manifeste, icône, fenêtre et mise en page vides, police console, tests. |
