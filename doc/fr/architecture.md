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
│  ├─ Kazo.Irc.Os/           # hôte : Kazo OS, ses apps de base et IRC
│  └─ Kazo.Irc.Protocol/     # le protocole IRC, sans dépendance au SDK Kazo (chantier 2)
├─ tests/
│  ├─ Kazo.Irc.Tests/        # tests unitaires C# de l'app (xUnit)
│  ├─ Kazo.Irc.Protocol.Tests/ # tests xUnit du protocole
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

## Protocole (`Kazo.Irc.Protocol`)

Une bibliothèque C# **pure** : aucune référence au SDK de Kazo OS ni à Blazor.
Elle se teste seule, et l'app lui donne un transport (WebSocket ou flux TCP du
relais, chantier 3). Elle suit RFC 1459 / RFC 2812 et les spécifications IRCv3
choisies par le plan (décision 18).

| Dossier | Contenu |
| --- | --- |
| `Messages/` | `IrcMessage` (analyse et écriture d'une ligne : tags IRCv3, source, commande, paramètres), `IrcTags` (échappement des valeurs), `IrcSource` (`nick!user@host` ou serveur), `IrcLimits` (512 octets, tags 4096 / 8191), `IrcNumerics`, `IrcProtocolException`. |
| `Text/` | `IrcEncoding` (UTF-8 en écriture ; en lecture UTF-8, repli Latin-1 par ligne), `IrcLineBuffer` (coupe un flux d'octets en lignes), `IrcTextSplitter` (coupe un texte long sans couper un caractère), `IrcCtcp` (`ACTION`, `VERSION`...), `IrcCaseMapping` (`rfc1459`, `strict-rfc1459`, `ascii`). |
| `Transport/` | `IIrcTransport` (connexion, lignes entrantes et sortantes, fermeture) et `FakeIrcTransport` (faux serveur des tests). |
| `Model/` | `IrcServerSupport` (ISUPPORT : `PREFIX`, `CHANTYPES`, `CHANMODES`, `CASEMAPPING`, `NETWORK`), `IrcChannel`, `IrcChannelUser`, `IrcUser`. |
| `Client/` | `IrcClient` (fichiers partiels par sujet), `IrcClientOptions`, `IrcReconnectPolicy`, `IrcClientState` et les données des événements. |
| `Input/` | `IrcInputParser` et `IrcInputCommand` : ce que l'utilisateur tape. |

### Messages

`IrcMessage.Parse` est tolérant (espaces en trop, ligne longue, commande en
minuscules, valeur de tag mal échappée) ; `IrcMessage.Format` est strict et
refuse ce qu'un serveur refuserait : CR, LF ou NUL dans un paramètre, paramètre
du milieu vide ou avec une espace, ligne de plus de 512 octets (CR LF compris),
tags client de plus de 4096 octets. Les longueurs sont comptées en **octets
UTF-8**, pas en caractères. Le dernier paramètre ne reçoit `:` que s'il en a
besoin.

### Transport

```csharp
public interface IIrcTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken); // null = fermé
    Task WriteLineAsync(string line, CancellationToken cancellationToken); // sans CR LF
    Task CloseAsync(CancellationToken cancellationToken);
}
```

Le client demande un **nouveau** transport à sa fabrique à chaque tentative de
connexion. Le chantier 3 écrit deux transports : WebSocket
(`KazoClientWebSocket`, sous-protocole `text.ircv3.net`, une trame = une ligne)
et le relais TCP (`KazoTcpClient`, lignes finies par CR LF et coupées par
`IrcLineBuffer`). `FakeIrcTransport` joue le serveur à la main :
`ServerSendAsync` rend la main quand le client a traité la ligne, et
`WaitForSentAsync` attend une ligne envoyée par le client.

### Client

`IrcClient` gère un réseau. Sa boucle de lecture traite les lignes une par une
et lève les événements depuis cette boucle (dans Blazor WebAssembly, c'est le
fil de l'interface).

| Sujet | Comportement |
| --- | --- |
| Enregistrement | `CAP LS 302`, `PASS` (si un mot de passe de serveur est donné), `NICK`, `USER`. |
| CAP | Lit la liste sur plusieurs lignes (`*`), demande seulement ce qu'il veut et que le serveur offre (`message-tags`, `server-time`, `away-notify`, `multi-prefix`, `account-notify`, `sasl`), gère `ACK`, `NAK`, `NEW`, `DEL`, puis `CAP END`. Un vieux serveur sans CAP (`421`) est accepté. |
| SASL | `PLAIN` seulement, et seulement si le serveur l'offre. Charge utile `compte\0compte\0mot de passe` en base64, en morceaux de 400 (`+` après un morceau plein). `903` réussi, `902`/`904`/`905`/`906` échoué : l'enregistrement continue et `SaslCompleted` le dit. Le mot de passe n'apparaît jamais dans `ToString`. |
| Pseudo pris | Pendant l'enregistrement, `433`, `432` et `436` essaient les pseudos de remplacement, puis `pseudo_`, `pseudo__`, `pseudo1` à `pseudo9` ; quand tous sont pris, `NICK_EXHAUSTED`, `QUIT` et pas de reconnexion. Après l'enregistrement, c'est une erreur montrée. |
| `001` | État `Connected` ; rejoint les salons automatiques (la première fois) et tous les salons où le client veut être, en aussi peu de lignes `JOIN` que la limite permet (salons à clé d'abord). |
| `PING`/`PONG` | Répond à `PING`. Après 90 s sans rien recevoir, envoie un `PING` ; 60 s plus tard sans réponse, la connexion est tenue pour perdue (`PING_TIMEOUT`) et la reconnexion commence. |
| Reconnexion | Délai croissant : 2 s, 4 s, 8 s... jusqu'à 5 minutes (`IrcReconnectPolicy`), remis au début après un enregistrement réussi ; nombre d'essais maximal possible. Après une perte, les salons restent connus (`WantsJoin`) et sont rejoints. `/part` et un kick enlèvent ce souhait. |
| `QUIT` | `QuitAsync` envoie `QUIT`, attend que le serveur ferme (3 s au plus) puis ferme elle-même ; pas de reconnexion. `DisposeAsync` arrête sans `QUIT`. |
| Horloge | Un `TimeProvider` (délais, minuterie du `PING`, heure des messages sans `server-time`) : les tests passent une fausse horloge. |

### Modèle

| Message | Effet |
| --- | --- |
| `005` | `IrcServerSupport` ; un changement de `CASEMAPPING` recalcule toutes les clés. |
| `JOIN`, `PART` (plusieurs salons), `KICK`, `QUIT`, `NICK` | Salons et utilisateurs à jour, un `IrcActivity` par conversation touchée (salons et conversation privée). `extended-join` donne le compte. |
| `353` / `366` | La liste `NAMES` est gardée jusqu'à sa fin puis remplace les utilisateurs ; avec `multi-prefix` un nom porte plusieurs symboles (`~@nick`) ; `nick!user@host` (userhost-in-names) est lu. |
| `MODE` / `324` | Modes d'utilisateur (`PREFIX`) et modes du salon selon les types A, B, C, D de `CHANMODES` ; la clé `+k` est gardée pour rejoindre. |
| `TOPIC`, `331`, `332`, `333` | Sujet, auteur et date. |
| `AWAY` (away-notify), `301`, `305`, `306`, `ACCOUNT` (account-notify) | Absence et compte des utilisateurs, absence du client. |
| `PRIVMSG`, `NOTICE` | `IrcChatMessage` avec l'heure `server-time`, la conversation (salon, `@#salon` compris, ou l'autre personne), message / action / avis. Un message privé ouvre une conversation privée ; un avis non. Les avis du serveur et ceux d'avant `001` vont à l'onglet du serveur. |
| CTCP | `ACTION` est une action ; `VERSION`, `PING`, `TIME`, `CLIENTINFO` reçoivent une réponse par `NOTICE` ; le reste (dont DCC, refusé par le plan) est seulement montré. |
| WHOIS | `311`, `312`, `313`, `317`, `319`, `330`, `301`, `401` réunis jusqu'à `318` dans un `IrcWhoisInfo`. |
| Autres numériques | `4xx`/`5xx` → `Error` ; les autres → `ServerText`. |

### Saisie

`IrcInputParser.Parse(ligne, conversation, chantypes)` lit le texte simple et
les commandes `/join` (`/j`), `/part`, `/msg`, `/me`, `/nick`, `/topic`,
`/whois`, `/away`, `/quit`, `/query`, `/raw` (`/quote`). `//texte` envoie
`/texte`. Une ligne impossible donne `Invalid` avec la raison (`UnknownCommand`,
`MissingArgument`, `NoConversation`, `NotAChannel`) : l'interface la traduit.
`IrcClient.ExecuteAsync` fait la commande. Un message long est coupé (à une
espace si possible) pour que la ligne relayée par le serveur, avec
`:nick!user@host ` devant, tienne dans 512 octets ; avant de connaître son hôte,
le client prend l'hôte le plus long (63 octets).

### Tests du protocole

`tests/Kazo.Irc.Protocol.Tests` (xUnit, `Microsoft.Extensions.TimeProvider.Testing`
pour la fausse horloge) : messages et tags, encodage, coupe des lignes et des
textes, CTCP, `CASEMAPPING`, ISUPPORT, saisie, et le client sur
`FakeIrcTransport` : CAP, SASL, pseudo pris, salons, modes, sujets, absence,
messages, CTCP, WHOIS, commandes, `QUIT`, reconnexion, `PING`. Couverture du
protocole : 97 % des lignes, 91 % des branches.

```bash
dotnet test Kazo.Irc.slnx
```

## Tests

| Suite | Commande | Contenu |
| --- | --- | --- |
| Protocole (xUnit) | `dotnet test Kazo.Irc.slnx` | voir « Tests du protocole » ci-dessus. |
| C# (xUnit) | `dotnet test Kazo.Irc.slnx -c Release` | manifeste (classe et `manifest.json` produit, textes français, icône), textes anglais et français complets, panneaux du mode compact, réglages (chemin, défauts, fichier cassé ou refusé, champ inconnu, écriture qui échoue). |
| Bout en bout | `npm run test:e2e` après `dotnet build Kazo.Irc.slnx -c Release` | dans l'hôte, profil neuf : trois colonnes côte à côte sur ordinateur dans le thème de l'OS ; police console gardée après fermeture et réouverture ; sur un Pixel 7, une colonne et les deux panneaux. |

Les tests de bout en bout démarrent l'hôte (`dotnet run --no-build -c Release`)
sur un port libre, ou utilisent `IRC_BASE_URL`.

## Avancement

| Chantier | Ce qu'il a ajouté ici |
| --- | --- |
| 1. `irc/1-solution` | Solution, hôte, CI, manifeste, icône, fenêtre et mise en page vides, police console, tests. |
| 2. `irc/2-protocol` | Bibliothèque `Kazo.Irc.Protocol` et ses tests (section « Protocole » ci-dessus). |

## Tâches du chantier 2

- [x] Projet `src/Kazo.Irc.Protocol` sans dépendance au SDK Kazo.
- [x] Analyse et écriture des messages : tags, source, paramètres, limites, UTF-8 et repli Latin-1.
- [x] `IIrcTransport` et `FakeIrcTransport`.
- [x] Client : enregistrement, CAP 302, SASL PLAIN, `PING`/`PONG`, pseudo pris, reconnexion avec délai croissant et retour dans les salons.
- [x] Modèle des salons et utilisateurs : préfixes, `NAMES`, `JOIN`, `PART`, `KICK`, `QUIT`, `NICK`, `TOPIC`, `MODE`, `AWAY`, `ACCOUNT`.
- [x] Commandes de saisie `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`.
- [x] Tests xUnit.
- [ ] Branchement à l'interface : chantiers 3 et 4 (le chantier 1 n'était pas fusionné quand le chantier 2 s'est terminé).
