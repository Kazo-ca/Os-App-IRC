> [English version](../en/architecture.md)

# Architecture de l'app IRC

## But

IRC est un client de discussion natif de Kazo OS pour les vrais réseaux IRC.
Le but et les décisions sont dans le [plan](plan.md). Ce document décrit le
code ; chaque chantier de la série `irc/base` le complète.

## Solution

```text
Os-App-IRC/
├─ Kazo.Irc.slnx
├─ src/
│  └─ Kazo.Irc.Protocol/        # le protocole IRC, sans dépendance au SDK Kazo (chantier 2)
└─ tests/
   └─ Kazo.Irc.Protocol.Tests/  # tests xUnit du protocole
```

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

### Tests

`tests/Kazo.Irc.Protocol.Tests` (xUnit, `Microsoft.Extensions.TimeProvider.Testing`
pour la fausse horloge) : messages et tags, encodage, coupe des lignes et des
textes, CTCP, `CASEMAPPING`, ISUPPORT, saisie, et le client sur
`FakeIrcTransport` : CAP, SASL, pseudo pris, salons, modes, sujets, absence,
messages, CTCP, WHOIS, commandes, `QUIT`, reconnexion, `PING`. Couverture du
protocole : 97 % des lignes, 91 % des branches.

```bash
dotnet test Kazo.Irc.slnx
```

## Tâches du chantier 2

- [x] Projet `src/Kazo.Irc.Protocol` sans dépendance au SDK Kazo.
- [x] Analyse et écriture des messages : tags, source, paramètres, limites, UTF-8 et repli Latin-1.
- [x] `IIrcTransport` et `FakeIrcTransport`.
- [x] Client : enregistrement, CAP 302, SASL PLAIN, `PING`/`PONG`, pseudo pris, reconnexion avec délai croissant et retour dans les salons.
- [x] Modèle des salons et utilisateurs : préfixes, `NAMES`, `JOIN`, `PART`, `KICK`, `QUIT`, `NICK`, `TOPIC`, `MODE`, `AWAY`, `ACCOUNT`.
- [x] Commandes de saisie `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`.
- [x] Tests xUnit.
- [ ] Branchement à l'interface : chantiers 3 et 4 (le chantier 1 n'était pas fusionné quand le chantier 2 s'est terminé).
