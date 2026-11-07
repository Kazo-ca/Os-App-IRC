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
├─ external/KazoOs/          # sous-module : Kazo OS sur le tag v3.2.0 (la version du dépôt)
├─ Directory.Build.props     # racine du sous-module, profil, import de Versions.props
├─ Directory.Build.targets   # mode source en Debug, KOSAPP0070 sans sous-module
├─ global.json
├─ Kazo.Irc.slnx
├─ src/
│  ├─ Kazo.Irc/              # l'app
│  ├─ Kazo.Irc.Os/           # hôte : Kazo OS, ses apps de base et IRC
│  ├─ Kazo.Irc.Demo/         # le réseau de démonstration, un serveur IRC en mémoire (chantier 4, jamais dans le Store)
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
| `IrcApp.cs` | Manifeste (`[AppManifest]`) : id `ca.kazo.irc`, nom « IRC », version `1.0.0`, éditeur `pub.kazo`, catégorie `communication` (décision 20), **ordinateur et mobile** (`MobileEntryPoint` = la même fenêtre, décision 7), `SingleInstancePerUser` (décision 8). Fenêtre de 960×620, redimensionnable jusqu'à 340×400. Capacités : `filesystem.private.read` et `.write` (réglages et réseaux, chantiers 1 et 3) ; `notifications.post`, `filesystem.user-home.read` et `.write` (notifications et journaux, chantier 4) ; `apps.launch` (chantier 5) ; `network.real.access` (dangereuse) et `network.simulated.write`, avec les ports réels `tcp/6697` et `tcp/6667` (`RealNetworkPorts`, chantier 3, ADR 0235 §3 de Kazo OS). |
| `IrcWindow.razor`, `.razor.cs`, `.razor.css` | La fenêtre unique : barre d'outils (titre et sujet, boutons « Journaux » et « Police console »), trois colonnes, panneaux du mode compact, ligne d'état. Elle tient un `IrcChat`, suit le focus de la fenêtre, tient le compteur du titre et envoie `QUIT` quand elle se ferme (`IWindowCloseGuard`, décision 8). |
| `Components/ChannelList` | Colonne de gauche : chaque réseau (pastille d'état), son onglet du serveur, ses salons et conversations privées, avec les pastilles de non-lus et de mentions et un × pour fermer ; les réseaux qu'on peut ouvrir, ceux du relais marqués « Serveur Kazo requis » (ou « Aucun serveur Kazo connecté ») ; le bouton « Gérer les réseaux ». |
| `Components/NetworkEditor` | L'éditeur des réseaux (chantier 3) : liste, ajout, modification, suppression ; transport, pseudo, compte SASL, salons. |
| `Net/` | Les fabriques des vrais transports : `KazoWebSocketTransportFactory` (WebSocket, sans serveur) et `KazoRelayTransportFactory` (relais TCP + TLS du serveur Kazo), avec la traduction des erreurs (chantier 3). |
| `Components/MessagePane` (`.razor`, `.razor.cs`, `.razor.css`, `.razor.js`) | Colonne du milieu : les lignes (`role="log"`, la plus récente en bas) et la ligne de saisie avec historique et complétion des pseudos. |
| `Components/UserList` | Colonne de droite : les utilisateurs du salon avec leur préfixe ; un clic ouvre une conversation privée. |
| `Chat/` | La discussion sans Blazor (voir « Discussion » plus bas) : `IrcChat`, `IrcSession`, `Conversation`, `ChatLine`, `IrcNetwork` et `IIrcTransportFactory`, `IrcMentions`, `IrcNick`, `InputHistory`, `NickCompleter`, `IIrcNotifier` / `KazoIrcNotifier`, `DemoNetwork` (sous `IRC_DEMO`). |
| `Logs/` | `IrcLog` (chemins, noms sûrs, format des lignes) et `IrcLogWriter` (écrit dans l'ordre). |
| `Layout/IrcLayout.cs`, `IrcPanel.cs` | Quel panneau latéral est ouvert en mode compact (aucun, salons ou utilisateurs ; un seul à la fois). |
| `Settings/IrcSettings.cs` | `~/.config/ca.kazo.irc/settings.json` : la police console (décision 19) et les journaux (`logsEnabled`, activés par défaut, décision 15). Un fichier absent, illisible ou cassé donne les valeurs par défaut ; un champ inconnu (ajouté par un chantier suivant) est ignoré. |
| `Settings/IrcNetworkList.cs`, `IrcNetworkDraft.cs` | `~/.config/ca.kazo.irc/networks.json` : les réseaux, les réseaux proposés, les vérifications de l'éditeur (chantier 3). |
| `Settings/IIrcSecretStore.cs` | Les mots de passe SASL dans le trousseau de l'OS (`KazoIrcSecretStore` sur `KazoKeyring`), jamais dans un fichier. |
| `Settings/IIrcFileSystem.cs`, `KazoIrcFileSystem.cs`, `AppScopedFileSystem.cs` | Lecture, écriture et ajout en fin des fichiers de l'app ; une version en mémoire sert aux tests. `AppScopedFileSystem` entre dans le contexte de l'app pour les écritures faites depuis la boucle de lecture du client (voir « Journaux »). |
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

## Discussion (chantier 4)

Le chantier 4 branche le client du protocole à la fenêtre, avec le réseau de
démonstration comme seul transport ; le chantier 3 ajoute les vrais.

```text
IrcWindow ──> IrcChat ──> IrcSession (une par réseau) ──> IrcClient ──> IIrcTransport
                 │              └─ Conversation (onglet du serveur, salons, privé) ──> ChatLine
                 ├─ IIrcNotifier (KazoIrcNotifier : notification de l'OS + son)
                 └─ IrcLogWriter ──> IIrcFileSystem (AppScopedFileSystem ──> KazoIrcFileSystem)
```

| Type | Rôle |
| --- | --- |
| `IrcNetwork`, `IrcNetworkCatalog`, `IIrcTransportFactory` | Un réseau a un `TransportKind` ; le catalogue donne au client une fonction qui crée un nouveau transport de ce genre à chaque tentative de connexion. Chantier 4 : genre `demo` seulement. Le chantier 3 ajoute `websocket` et `relay`. |
| `IrcSession` | Un réseau : son `IrcClient` et ses conversations, remplies par les événements du client. Un message va à son salon ou à sa conversation privée ; un avis privé va là où cette personne parle déjà, sinon à l'onglet du serveur ; les numériques, le MOTD, le WHOIS, les erreurs et l'état de la connexion vont à l'onglet du serveur. Une conversation privée suit un changement de pseudo. |
| `Conversation` | L'onglet du serveur, un salon ou une conversation privée : ses 1000 dernières lignes (`MaxLines`), `UnreadCount`, `HighlightCount`, ses utilisateurs et son sujet tirés du modèle du client. |
| `ChatLine` | Heure, genre (message, action, avis, activité, serveur, erreur), pseudo, texte, `IsOwn`, `IsMention`. |
| `IrcChat` | Les réseaux, la conversation ouverte, les lignes tapées (`IrcInputParser` puis `IrcClient.ExecuteAsync` ; une ligne refusée donne un `ChatInputProblem` que la fenêtre traduit), les compteurs, les notifications et les journaux. `/join` ouvre le salon quand le serveur le confirme (pas après une reconnexion) ; `/query` ouvre la conversation privée. |
| `IrcNick` | Le pseudo par défaut (décision 12) : le nom d'utilisateur de l'OS, en gardant les caractères permis dans un pseudo, 16 au plus. |

### Ce qu'on dit à l'utilisateur (décision 14)

| Événement | Effet |
| --- | --- |
| Un message d'un autre dans une conversation qui n'est pas ouverte, ou quand la fenêtre n'a pas le focus | `UnreadCount` + 1 : la conversation est en gras dans la liste, avec une pastille grise. |
| Une **mention** (`IrcMentions` : le pseudo en mot entier, toute casse) ou un **message privé**, pas vu | `HighlightCount` + 1 : la conversation passe à la couleur d'avertissement avec une pastille ambre, et le titre de la fenêtre devient « (N) IRC », N = toutes les mises en évidence (`IrcChat.AttentionCount`). La ligne de la mention est mise en évidence dans la conversation. |
| La même chose quand la fenêtre n'a **pas** le focus (une autre fenêtre active, ou réduite) | Une notification de l'OS (« kazo-bot dans #kazo », « kazo-bot (privé) »), que l'OS montre en bulle avec le son des notifications. Les avis ne notifient jamais. |
| La conversation est ouverte et la fenêtre reprend le focus | Les deux compteurs reviennent à 0 ; le titre redevient « IRC ». |

Le focus vient des événements de fenêtre du SDK (`OnWindowActivated`,
`OnWindowDeactivated`, `OnWindowStateChanged`). Une conversation privée
commencée par quelqu'un d'autre n'éloigne pas l'utilisateur de la conversation
ouverte.

### Ligne de saisie

Entrée envoie ; ↑ et ↓ parcourent les 100 dernières lignes (`InputHistory`, la
ligne en cours est gardée) ; Tab complète le dernier mot avec les utilisateurs
du salon (ou l'autre personne) et passe d'une correspondance à l'autre, avec
`": "` au début de la ligne (`NickCompleter`). Blazor ne sait pas annuler une
seule touche : `MessagePane.razor.js` annule l'action par défaut de Tab, et
seulement quand la saisie contient du texte ; une saisie vide laisse Tab en
sortir. Les lignes suivent la plus récente sans script
(`flex-direction: column-reverse`), et seules les 400 dernières sont rendues.

### Journaux (décision 15)

| Règle | Valeur |
| --- | --- |
| Fichier | `~/.local/share/ca.kazo.irc/logs/<réseau>/<conversation>/AAAA-MM-JJ.log`, le jour et les heures dans le fuseau de l'OS (`AppContext.TimeZone`). |
| Lignes | `[HH:MM:SS] <pseudo> texte`, `[HH:MM:SS] * pseudo action`, `[HH:MM:SS] -pseudo- avis`, `[HH:MM:SS] *** pseudo joined` (et left, quit, is now known as, changed the topic, sets mode, is away, is back : le format des journaux ne dépend pas de la langue). L'onglet du serveur et les lignes du serveur ne sont pas écrits. |
| Noms | `IrcLog.SafeName` : `/`, `\`, `:`, `*`, `?`, `"`, `<`, `>`, `|` et les caractères de contrôle deviennent `_` ; un point au début devient `_` (donc jamais `.`, `..` ni un dossier caché) ; jamais vide ; 64 caractères au plus. Le nom de la conversation est ramené à une casse selon les règles du réseau (`#Kazo` et `#kazo` partagent un dossier). |
| Ordre et erreurs | `IrcLogWriter` écrit une ligne après l'autre. Le premier échec est montré une fois sur la ligne d'état et n'arrête jamais la discussion ni les lignes suivantes. |
| Option | Le bouton « Journaux » (`aria-pressed`) enregistre `logsEnabled` dans `settings.json`. |
| Contexte | Les lignes arrivent de la boucle de lecture du client, que l'OS n'a pas lancée : `KazoFile` n'y trouverait pas de contexte d'app. `AppScopedFileSystem` y entre (`KazoExecutionContext.Enter`) pour chaque opération. |

Une autre app qui lit un journal juste après l'écriture d'une ligne peut la voir
quelques secondes plus tard (système de fichiers de l'OS) ; le test de bout en
bout relit.

### Réseau de démonstration

`Kazo.Irc.Demo` contient `DemoIrcServer`, un `IIrcTransport` qui joue un petit
serveur IRC en mémoire (sans référence au SDK). Il enregistre le client (CAP,
numériques de bienvenue, `005`, MOTD qui dit quoi essayer), répond à `PING`,
`JOIN` (sujet et `NAMES` avec `@kazo-bot`, `+morpheus`, `trinity`), `PART`,
`TOPIC`, `WHOIS`, `AWAY`, `NICK`, `MODE`, `QUIT`, refuse le pseudo du robot
(`433`) et les commandes inconnues (`421`). Son robot, `kazo-bot` :

| Vous écrivez | Le robot |
| --- | --- |
| un message de salon qui le nomme | répond tout de suite et vous nomme (une mention) |
| `!mention N` dans un salon | vous nomme dans ce salon N secondes plus tard (3 par défaut, 60 au plus) |
| `!private N` dans un salon | vous envoie un message privé N secondes plus tard |
| `!links` dans un salon | envoie deux adresses web, du texte en gras, en couleur et en italique, et du texte qui ressemble à du HTML (chantier 5) |
| un message privé | répond en privé |

Il est proposé sous le nom « Kazo Demo » seulement quand l'app est compilée
avec `IrcDemoNetwork=true` : par défaut en Debug, et dans la CI avec
`dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true` pour les tests
de bout en bout. `Kazo.Irc` référence alors `Kazo.Irc.Demo` et définit
`IRC_DEMO` (`Chat/DemoNetwork.cs`, `Chat/IrcNetworks.cs`). Le Store et les
paquets compilent Release sans lui : le paquet n'a ni `Kazo.Irc.Demo.dll` ni le
code de démonstration.

## Connexion aux vrais réseaux (chantier 3)

Deux chemins vers un vrai réseau (décisions 3, 4 et 11, ADR 0235 de Kazo OS),
choisis par réseau dans `networks.json` (`transport`) :

| Transport | Code | Serveur Kazo | Comment |
| --- | --- | --- | --- |
| `websocket` | `Net/KazoWebSocketTransportFactory` → `WebSocketIrcTransport` | **pas besoin** | `KazoClientWebSocket` (le WebSocket du navigateur, ADR 0235 §1) avec le sous-protocole `text.ircv3.net` : un message WebSocket = une ligne, sans CR LF. Un message en plusieurs trames est remis ensemble ; un message de plus de 16 Kio est jeté ; un message binaire ou en Latin-1 passe par le repli d'`IrcEncoding`. |
| `relay` | `Net/KazoRelayTransportFactory` → `StreamIrcTransport` | **requis** | `KazoTcpClient.ConnectAsync(hôte, port, new KazoTcpConnectOptions { UseTls, TargetHost }, ct)` : le relais du serveur Kazo ouvre TCP, et TLS quand `tls` est vrai (6697 par défaut, décision 4) en vérifiant le certificat. Lignes finies par CR LF, coupées par `IrcLineBuffer`. |
| `demo` | `Chat/DemoNetwork.cs` | non | Le réseau de démonstration en mémoire, seulement avec `IrcDemoNetwork=true` (Debug, E2E). |

Les deux transports réels demandent `network.real.access` (capacité
dangereuse : un administrateur l'approuve au premier lancement, avec les ports
`tcp/6697` et `tcp/6667` déclarés par `IrcApp.RealNetworkPorts`) ; le relais
demande aussi `network.simulated.write` (normale), parce que `KazoTcpClient`
ouvre une socket de la pile réseau de l'OS. Après l'approbation, l'accord avec
ses ports part au serveur Kazo connecté (ADR 0233), dont `BlockedPorts` garde
le dernier mot. L'éditeur refuse un autre port pour le relais
(`IrcNetworkList.DeclaredPorts`). Chaque connexion entre dans le contexte de
l'app (`KazoExecutionContext.Enter`), parce que la boucle du client tourne hors
des flux de l'OS.

**Erreurs claires.** Chaque fabrique traduit les erreurs du SDK en
`IrcTransportException` (bibliothèque du protocole), avec un code
d'`IrcTransportErrors` et `IsPermanent`. Le client montre le code
(`IrcClientError.Code`) ; un échec permanent arrête la reconnexion, un autre
est réessayé avec le délai croissant. `IrcSession` remplace le code par une
phrase traduite (`IrcChatTexts.TransportErrors`, clés `Error*` des
ressources) :

| Ce qui arrive | Code | Réessayé |
| --- | --- | --- |
| l'app n'a pas `network.real.access` (`UnauthorizedAccessException`) | `NO_PERMISSION` | non |
| relais : « hôte introuvable » (pas de serveur Kazo, relais arrêté, ou l'accord ne lui est pas encore arrivé) | `NO_SERVER` | non |
| relais : connexion refusée (le serveur IRC, ou un port dans `BlockedPorts`) | `REFUSED` | oui |
| relais : certificat TLS refusé (`AuthenticationException`) | `CERTIFICATE_REFUSED` | non |
| WebSocket : adresse du réseau simulé (refusée par l'OS) | `SIMULATED_HOST` | non |
| autre échec (le navigateur ne dit pas pourquoi un WebSocket échoue) | `UNREACHABLE` | oui |
| connexion perdue après l'ouverture | `LOST` | oui |
| pas de WebSocket ni de TCP ici (Web Worker) | `NOT_SUPPORTED` | non |

**Détecter le serveur Kazo.** Une app ne peut pas demander à l'OS si un serveur
Kazo est connecté sans `server.connection.manage` (dangereuse). IRC **essaie
donc à la demande** (choix de Benoit, 2026-11-07) : les réseaux `relay` sont
marqués « Serveur Kazo requis » dans la liste ; un échec `NO_SERVER` les marque
« Aucun serveur Kazo connecté » (`IrcChat.RelayUnavailable`) jusqu'au prochain
essai. Une API légère du SDK est demandée dans [Os#418](https://github.com/Kazo-ca/Os/issues/418).
Une session arrêtée (échec permanent ou `QUIT`) peut être rouverte depuis le
bouton « + Se connecter » : `IrcChat.Connect` crée une nouvelle session avec les
réglages du moment.

**Réseaux proposés** (`IrcNetworkList.Proposed`, au premier lancement) :

| Réseau | Transport | Pourquoi |
| --- | --- | --- |
| Libera.Chat | `relay`, `irc.libera.chat:6697` TLS | pas de WebSocket IRC public : sa passerelle web (`web.libera.chat`) refuse les autres origines. |
| OFTC | `relay`, `irc.oftc.net:6697` TLS | pas de WebSocket IRC : son webchat passe par Socket.IO, pas par `text.ircv3.net`. |
| UnrealIRCd | `websocket`, `wss://irc.unrealircd.org/` | accepte `text.ircv3.net` (vérifié le 2026-11-07 : poignée de main WebSocket et enregistrement IRC depuis le conteneur de développement ; [documentation WebSocket d'UnrealIRCd](https://www.unrealircd.org/docs/WebSocket_support)). Fonctionne sans serveur Kazo. |

**Éditeur des réseaux** (`Components/NetworkEditor`, bouton « Gérer les
réseaux » sous la liste) : nom, transport (WebSocket, ou « Par le serveur
Kazo » avec hôte, port et TLS), pseudo (vide = le nom d'utilisateur de l'OS,
décision 12), compte et mot de passe SASL, salons rejoints à la connexion.
`IrcNetworkDraft` tient les champs ; `IrcNetworkList.Validate` refuse un nom
vide ou pris, une adresse qui n'est pas `wss://` ou `ws://`, un hôte vide, un
port hors des ports déclarés, un pseudo ou un salon invalide. Il écrit
`~/.config/ca.kazo.irc/networks.json` (`IrcNetworkList.SaveAsync`) ; un
fichier absent ou cassé donne les réseaux proposés.

**Mot de passe SASL.** Jamais dans `networks.json` : il va dans le trousseau de
l'OS (`KazoKeyring`, entrée `sasl/<id du réseau>`, `KazoIrcSecretStore`, sans
capacité). Le champ vide garde le mot de passe enregistré ; retirer le compte
l'efface. Un trousseau verrouillé est dit dans la ligne d'état.

## Liens et mise en forme (chantier 5)

Le texte d'une ligne (message, action, avis, activité, sujet du salon dans la
barre) passe par le composant `Components/MessageText.razor`, qui dessine les
codes de mise en forme IRC et rend les adresses web cliquables (décision 17).

| Élément | Rôle |
| --- | --- |
| `IrcFormatting` (`Kazo.Irc.Protocol/Text`) | Lit les codes `\x02` gras, `\x1D` italique, `\x1F` souligné, `\x1E` barré, `\x11` chasse fixe, `\x16` inversé, `\x0F` remise à zéro, `\x03` couleur mIRC (`fg[,bg]`, un ou deux chiffres, 99 = par défaut) et `\x04` couleur hexadécimale, en suites de texte de même style (`IrcTextRun`, `IrcTextStyle`). `Strip` retire les codes. |
| `MessageLinks` (`Kazo.Irc/Text`) | Trouve les adresses `http://` et `https://` : pas au milieu d'un mot, sans nom d'utilisateur (`https://kazo.ca@evil.example`), fin à un code de mise en forme ou au premier blanc ou caractère `< > " \` { } \| ^`, sans la ponctuation de fin de phrase (`. , ; : ! ? ' " * …` et guillemets) ni une parenthèse ou un crochet fermant sans ouvrant dans l'adresse (`(https://kazo.ca)` mais `…/IRC_(protocol)` garde la sienne). Seuls `http` et `https` deviennent des liens : jamais `javascript:`, `data:`, `file:` ; `irc://` attend [#2](https://github.com/Kazo-ca/Os-App-IRC/issues/2). |
| `MessageSegments` (`Kazo.Irc/Text`) | Assemble les deux : les suites de style découpées aux bords des liens ; un code de mise en forme termine une adresse (`\x02https://kazo.ca\x02hello` ne lie que `https://kazo.ca`). |
| `MessageText` | Chaque suite est un nœud de texte (Razor encode) : rien du réseau ne devient du HTML, `<b>` s'affiche tel quel. Un lien est un `<a role="link" tabindex="0">` **sans `href`**, avec l'adresse absolue échappée (`Uri.AbsoluteUri`) dans l'info-bulle : un clic du milieu ou « ouvrir dans un nouvel onglet » ne peut pas donner l'adresse au navigateur hôte ; un clic ou Entrée la passe à la fenêtre. Les styles sont des classes de `MessageText.razor.css` (`irc-b`, `irc-fg-4`, `irc-bg-1`…), jamais du style en ligne (ADR 0007) : les 16 couleurs mIRC standard sont dessinées ; les couleurs étendues (16 à 98) et hexadécimales gardent les couleurs par défaut. |
| `IrcWindow.OpenLinkAsync` | Ouvre l'adresse dans une nouvelle fenêtre du Navigateur de l'OS (`KazoBrowser.OpenAsync`, capacité `apps.launch`) ; un échec s'affiche dans la barre d'état. |

Les codes sont aussi retirés là où le texte doit être brut : lignes des
journaux, texte des notifications, recherche du pseudo pour les mentions, et
l'info-bulle du sujet.

Au premier lancement du Navigateur dans un profil neuf, Kazo OS 3.0.0 demande à
l'administrateur d'approuver la capacité `network.real.access` **du
Navigateur** (pas d'IRC) ; le test de bout en bout l'approuve.

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
connexion. Le chantier 3 a écrit deux transports : WebSocket
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
textes, CTCP, codes de mise en forme, `CASEMAPPING`, ISUPPORT, saisie, et le client sur
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
| C# (xUnit) | `dotnet test Kazo.Irc.slnx -c Release` | manifeste (classe et `manifest.json` produit, textes français, icône, capacités), textes anglais et français complets, panneaux du mode compact, réglages (chemin, défauts, fichier cassé ou refusé, champ inconnu, écriture qui échoue, option des journaux) ; discussion sur le serveur de démonstration avec une fausse horloge (connexion, salon, messages, mentions, messages privés, non-lus, compteur du titre, notifications avec et sans le focus, lignes refusées, fermeture, `QUIT`) ; mentions, pseudo par défaut, historique, complétion ; journaux (chemins, noms hostiles, format, fuseau, option, écriture qui échoue, codes retirés) ; le serveur de démonstration ; liens (ponctuation, parenthèses, schémas refusés, balises et guillemets venus du réseau), segments et classes CSS des styles, notification sans codes ; vrais réseaux (chantier 3) : fabriques WebSocket et relais (adresse, hôte, traduction de chaque erreur), `networks.json` (réseaux proposés, fichier cassé ou refusé, aller-retour sans mot de passe, entrées inutilisables, vérifications, id, salons), champs de l'éditeur et mot de passe SASL, chat (« aucun serveur Kazo », session rouverte, échec réessayé, SASL passé au client). Protocole : transports WebSocket (faux `WebSocket`) et flux (faux flux), échec permanent non réessayé, `MessageHandled` après la mise à jour du modèle ; chat : les utilisateurs et le sujet envoyés après le `JOIN` redessinent la fenêtre. |
| Bout en bout | `npm run test:e2e` après `dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true` | dans l'hôte, profil neuf : trois colonnes côte à côte sur ordinateur dans le thème de l'OS ; police console gardée après fermeture et réouverture ; sur un Pixel 7, une colonne et les deux panneaux ; `chat.e2e.mjs` : connexion au réseau de démonstration, `/join #kazo`, envoyer et recevoir, historique et Tab, une mention pendant que le Terminal a le focus donne une notification de l'OS et « (1) IRC », le journal lu avec `cat` dans le Terminal, un message privé compté, fermeture de la fenêtre, la discussion sur un Pixel 7 ; `!links` : deux liens sans la ponctuation autour, gras, couleur et italique dessinés, balises affichées en texte, le clic ouvre le Navigateur de l'OS sur l'adresse sans quitter la page ; `websocket.e2e.mjs` (chantier 3) : approbation de `network.real.access` par l'administrateur, notes « Serveur Kazo requis », réseau ajouté dans l'éditeur et connecté par WebSocket à un serveur IRC joué par le test (`routeWebSocket` de Playwright, aucun vrai réseau), salon automatique, envoyer et recevoir ; Libera.Chat sans serveur Kazo donne le message clair et « Aucun serveur Kazo connecté ». Le relais n'a pas d'essai de bout en bout : l'hôte n'est pas un serveur Kazo. |

Les tests de bout en bout démarrent l'hôte (`dotnet run --no-build -c Release`)
sur un port libre, ou utilisent `IRC_BASE_URL`.

## Avancement

| Chantier | Ce qu'il a ajouté ici |
| --- | --- |
| 1. `irc/1-solution` | Solution, hôte, CI, manifeste, icône, fenêtre et mise en page vides, police console, tests. |
| 2. `irc/2-protocol` | Bibliothèque `Kazo.Irc.Protocol` et ses tests (section « Protocole » ci-dessus). |
| 3. `irc/3-connect` | Vrais réseaux par WebSocket et par le relais du serveur Kazo, erreurs claires, `networks.json` et son éditeur, mot de passe SASL dans le trousseau, sous-module sur v3.2.0 (section « Connexion aux vrais réseaux » ci-dessus). |
| 4. `irc/4-notifications-and-logs` | La discussion branchée à la fenêtre sur le réseau de démonstration, notifications et compteur du titre, journaux (section « Discussion » ci-dessus). |
| 5. `irc/5-links` | Adresses web cliquables ouvertes dans le Navigateur, codes de mise en forme IRC dessinés ou retirés (section « Liens et mise en forme » ci-dessus). |

## Tâches du chantier 2

- [x] Projet `src/Kazo.Irc.Protocol` sans dépendance au SDK Kazo.
- [x] Analyse et écriture des messages : tags, source, paramètres, limites, UTF-8 et repli Latin-1.
- [x] `IIrcTransport` et `FakeIrcTransport`.
- [x] Client : enregistrement, CAP 302, SASL PLAIN, `PING`/`PONG`, pseudo pris, reconnexion avec délai croissant et retour dans les salons.
- [x] Modèle des salons et utilisateurs : préfixes, `NAMES`, `JOIN`, `PART`, `KICK`, `QUIT`, `NICK`, `TOPIC`, `MODE`, `AWAY`, `ACCOUNT`.
- [x] Commandes de saisie `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`.
- [x] Tests xUnit.
- [x] Branchement à l'interface : fait au chantier 4 (avec le réseau de démonstration) ; les vrais transports viennent au chantier 3.

## Tâches du chantier 3

- [x] Sous-module `external/KazoOs` et paquets `Kazo.Os.*` sur v3.2.0. Os#407 et Os#408 restent ouvertes : les contournements (`KazoIrcFileSystem`, `.mobile-shell .irc-toolbar`) sont gardés.
- [x] `WebSocketIrcTransport` et `StreamIrcTransport` (protocole), `IrcTransportException` ; un échec permanent arrête la reconnexion.
- [x] Fabriques `KazoWebSocketTransportFactory` et `KazoRelayTransportFactory`, erreurs traduites en phrases claires.
- [x] Capacités `network.real.access` et `network.simulated.write`, ports `tcp/6697` et `tcp/6667`.
- [x] Serveur Kazo essayé à la demande, notes « Serveur Kazo requis » et « Aucun serveur Kazo connecté » ; [Os#418](https://github.com/Kazo-ca/Os/issues/418) pour une API de détection.
- [x] Réseaux proposés (Libera.Chat, OFTC, UnrealIRCd par WebSocket, vérifiés), `networks.json`, éditeur.
- [x] Pseudo par défaut = nom d'utilisateur de l'OS ; mot de passe SASL dans `KazoKeyring`.
- [x] Fenêtre redessinée après les réponses qui changent un salon sans ligne (`IrcClient.MessageHandled` : fin de `NAMES`, sujet, modes), qu'un vrai réseau envoie après que le `JOIN` est dessiné.
- [x] Tests xUnit et Playwright (WebSocket par `routeWebSocket`, approbation de l'administrateur) ; le relais sans essai de bout en bout (pas de serveur Kazo de test).
- [x] Docs (fr, en), README, avancement du plan.

## Tâches du chantier 4

- [x] Une session par réseau qui tient un `IrcClient` ; fabrique de transports (`IIrcTransportFactory`) que le chantier 3 complète.
- [x] Réseau de démonstration en mémoire (`Kazo.Irc.Demo`), seulement avec `IrcDemoNetwork` (Debug, E2E de la CI), jamais dans le paquet du Store.
- [x] Liste des réseaux et conversations, vue des messages (heure, pseudo, `/me`, avis, lignes du serveur), utilisateurs avec préfixes.
- [x] Saisie : commandes de la décision 18 par `ExecuteAsync`, historique (↑/↓), complétion des pseudos (Tab) ; ordinateur et mobile.
- [x] Fermer la fenêtre envoie `QUIT` (décision 8).
- [x] Mentions et messages privés : notification de l'OS et son sans le focus, titre « (N) IRC », pastilles de non-lus et de mentions (décision 14).
- [x] Journaux par jour dans `~/.local/share/ca.kazo.irc/logs/`, noms sûrs, option `logsEnabled` (décision 15).
- [x] Capacités `notifications.post`, `filesystem.user-home.read` et `.write`.
- [x] Tests xUnit et Playwright ; docs (fr, en), avancement du plan.

## Tâches du chantier 5

- [x] Codes de mise en forme IRC lus en styles (`IrcFormatting`, bibliothèque du protocole) et retirés dans les journaux, les notifications et la recherche des mentions.
- [x] Adresses `http://` et `https://` trouvées sans la ponctuation finale (`MessageLinks`).
- [x] Rendu par `MessageText` : texte seulement (aucun HTML venu du réseau), liens `<a>` interceptés, styles en classes CSS (16 couleurs mIRC).
- [x] Clic : nouvelle fenêtre du Navigateur de l'OS (`KazoBrowser.OpenAsync`), capacité `apps.launch`.
- [x] Commande `!links` du robot de démonstration.
- [x] Tests xUnit (formatage, liens, segments, classes, journaux, notifications) et Playwright (clic qui ouvre le Navigateur).
- [x] Docs (fr, en), avancement du plan.
