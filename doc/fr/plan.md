> [English version](../en/plan.md)

# Plan : l'app IRC pour Kazo OS

Plan **validé le 2026-11-07** par Benoit (fil « Planifier l'app IRC », plan
fusionné par la PR #3). Ce document fixe le but, les décisions et l'ordre des
choses à faire ; l'avancement est en fin de page.

## But

IRC est un **client de discussion natif de Kazo OS** pour les vrais réseaux
IRC (Libera.Chat, OFTC, etc.), écrit en C# et Blazor, sur ordinateur et sur
mobile. Il se connecte comme un vrai client :

- **sans le serveur optionnel**, par WebSocket, aux réseaux qui acceptent IRC
  par WebSocket ;
- **avec le serveur**, par son relais en TCP (TLS sur le port 6697), aux
  réseaux qui ne l'acceptent pas.

C'est une app tierce dans son propre dépôt, qui suit le modèle
`kazoos-solution`, comme Chess et Webamp, et qui sera publiée dans le Kazo OS
Store.

## Décisions

Réponses de Benoit le 2026-11-07 (« ok » ou « d'accord » = la recommandation).

| # | Sujet | Décision |
| --- | --- | --- |
| 1 | But | Parler sur de **vrais réseaux IRC**. |
| 2 | Base du client | **Client natif C# / Blazor**, mise en page inspirée de KiwiIRC (KiwiIRC lui-même n'est pas utilisé : Vue 2 n'est plus maintenu). Le WebSocket est gardé pour ne pas dépendre du serveur ; une option, disponible seulement quand le serveur est connecté, permet de joindre les réseaux qui n'acceptent pas WebSocket. |
| 3 | Transport | **Les deux** : WebSocket direct (`KazoClientWebSocket`, nouveau dans le SDK) quand le réseau l'accepte ; relais TCP du serveur (`KazoTcpClient`) sinon. [ADR 0235](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0235-real-network-for-chat-apps.md) de Kazo OS. |
| 4 | TLS | Le **relais ouvre TLS** vers le serveur IRC ; l'app utilise 6697 par défaut. Changement de l'OS (ADR 0235 §2). |
| 5, 9 | Ports | L'app **déclare ses ports** (6697, 6667) dans son manifeste ; l'approbation de `network.real.access` les montre ; le relais les accepte sauf si sa politique les refuse (`BlockedPorts`). ADR 0235 §3. |
| 6 | Serveur IRC simulé | Il doit passer par le TCP : pas dans la première version. Plus tard : [#1](https://github.com/Kazo-ca/Os-App-IRC/issues/1), bloquée par [Os#393](https://github.com/Kazo-ca/Os/issues/393) (TCP vers un hôte simulé). |
| 7 | Mobile | Ordinateur **et** mobile dès la première version (une colonne sur mobile). |
| 8 | Fenêtre | Une seule fenêtre (`SingleInstancePerUser`) avec tous les réseaux et salons. Elle reste connectée quand elle est réduite ; la fermer déconnecte proprement (`QUIT`). |
| 10 | Changements de l'OS | Une ADR (0235) en trois sections et une série `real-network/base` dans Kazo OS : [Os#395](https://github.com/Kazo-ca/Os/issues/395). |
| 11 | Capacité du WebSocket | La même `network.real.access` que le relais : un seul accord « cette app parle au vrai réseau ». |
| 12 | Pseudo et mot de passe | Pseudo par défaut = nom d'utilisateur de l'OS. Connexion SASL PLAIN ; le mot de passe est gardé dans le trousseau chiffré de l'OS (`KazoKeyring`), jamais dans un fichier en clair. |
| 13 | Réseaux proposés | Une courte liste modifiable : Libera.Chat, OFTC, et un réseau qui accepte WebSocket. Un réseau sans WebSocket est marqué « serveur Kazo requis ». Le chantier 3 vérifie quels réseaux acceptent WebSocket. **Chantier 3 :** Libera.Chat et OFTC n'en ont pas (relais) ; UnrealIRCd (`wss://irc.unrealircd.org/`) en a un, choisi par Benoit le 2026-11-07. |
| 14 | Mentions et messages privés | Quand la fenêtre n'a pas le focus : notification de l'OS, son des notifications, compteur de non-lus dans le titre de la fenêtre. Salons non lus mis en évidence dans la liste. |
| 15 | Journaux | Activés par défaut, en texte dans `~/.local/share/ca.kazo.irc/logs/<réseau>/<salon>/AAAA-MM-JJ.log` ; une option les coupe. |
| 16 | Liens `irc://` | Kazo OS ne sait pas encore associer un schéma d'URL à une app : [Os#394](https://github.com/Kazo-ca/Os/issues/394), liée à [#2](https://github.com/Kazo-ca/Os-App-IRC/issues/2). |
| 17 | Fichiers et liens | Pas de DCC (il faudrait accepter des connexions entrantes). Les adresses web des messages s'ouvrent dans le Navigateur de l'OS. |
| 18 | Fonctions IRC | Protocole de base + IRCv3 courant : CAP, SASL, server-time, message-tags, away-notify, multi-prefix, account-notify. Commandes `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`. |
| 19 | Apparence | Suit le thème de l'OS (look moderne), police à chasse fixe en option pour l'aspect « console ». |
| 20 | Identité | Id `ca.kazo.irc`, nom « IRC », éditeur `pub.kazo`, catégorie `communication`, version `1.0.0`, licence MIT. |
| 21 | Documents | Ce plan dans ce dépôt (fr + en) ; l'ADR 0235 dans Kazo OS ; la ligne IRC de `features.md` dit « client natif ». |

## L'app

| Élément | Valeur |
| --- | --- |
| Manifeste | `[AppManifest]` `IrcApp` : fenêtre, ordinateur et mobile, `SingleInstancePerUser`, ports réels déclarés `tcp/6697` et `tcp/6667` (forme exacte fixée par l'ADR 0235 au code). |
| Capacités | `network.real.access` (dangereuse : approuvée à l'installation, avec les ports), `notifications.post`, `filesystem.private.read` et `.write` (`~/.config/ca.kazo.irc/`), `filesystem.user-home.write` (journaux dans `~/.local/share/ca.kazo.irc/`), `apps.launch` (ouvrir une adresse dans le Navigateur). Le mot de passe SASL va dans le trousseau de l'app, sans capacité. |
| Transports | Une interface `IIrcTransport` (lignes de texte entrantes et sortantes) avec trois implémentations : WebSocket (`KazoClientWebSocket`, sous-protocole `text.ircv3.net`), relais TCP (`KazoTcpClient`, TLS par le relais), et une fausse pour les tests. Le choix est fait par réseau ; l'option « par le serveur Kazo » n'est proposée que quand le serveur est connecté. **Changé au chantier 3 (choix de Benoit, 2026-11-07) :** une app ne voit pas le serveur sans `server.connection.manage` (dangereuse) ; l'option est donc toujours proposée et **essayée à la demande** : « Serveur Kazo requis », puis « Aucun serveur Kazo connecté » après un échec. Une API de détection est demandée à Kazo OS ([Os#418](https://github.com/Kazo-ca/Os/issues/418)). |
| Protocole | Une bibliothèque C# dans la solution : analyse et écriture des messages (tags IRCv3 compris), négociation CAP, SASL PLAIN, `PING`/`PONG`, reconnexion avec délai croissant, et rejoindre les salons ouverts après une reconnexion. |
| Interface | Ordinateur : trois colonnes (réseaux et salons, messages, utilisateurs du salon), saisie avec historique et complétion des pseudos (Tab). Mobile : une colonne, la liste des salons et celle des utilisateurs en panneaux. |
| Réglages | `~/.config/ca.kazo.irc/networks.json` : réseaux (adresse, port, transport, TLS, pseudo, compte SASL, salons rejoints automatiquement) ; `~/.config/ca.kazo.irc/settings.json` : journaux, police, notifications. |
| Journaux | Une ligne par message, horodatée (`[HH:MM:SS] <pseudo> texte`), un fichier par jour. |

## Choses à faire, dans l'ordre

Série `irc/base` (une branche `irc/<n>-<sujet>` et une PR par chantier).

0. **Prérequis Kazo OS** : l'ADR 0235 codée ([Os#395](https://github.com/Kazo-ca/Os/issues/395), série `real-network/base`) et publiée dans une version de l'OS. Les chantiers 1 et 2 n'en dépendent pas.
1. `irc/1-solution` : solution `kazoos-solution` sur Kazo OS 3.0.0, manifeste, fenêtre unique, mise en page ordinateur et mobile, thème et police, `THIRD-PARTY-NOTICES.md`.
2. `irc/2-protocol` : bibliothèque du protocole (messages, CAP, SASL, IRCv3, commandes), interface `IIrcTransport` et faux transport, salons et utilisateurs, branchée sur l'interface avec un faux serveur.
3. `irc/3-connect` (après la version de l'OS avec l'ADR 0235) : transports WebSocket et relais TCP + TLS, liste des réseaux et éditeur, pseudo, mot de passe SASL dans le trousseau, reconnexion, choix du transport selon la présence du serveur.
4. `irc/4-notifications-and-logs` : notifications des mentions et messages privés, compteur dans le titre, salons non lus, journaux en fichiers et leur option.
5. `irc/5-links` : adresses web ouvertes dans le Navigateur ; les liens `irc://` attendent [#2](https://github.com/Kazo-ca/Os-App-IRC/issues/2).
6. `irc/6-store` : dossier `store/` (icône, descriptions fr et en qui expliquent WebSocket et serveur Kazo, captures, catégorie `communication`), tests de bout en bout, tag `v1.0.0`, PR au catalogue (signature seulement dans le dépôt Os-AppMarketPlace).

## Tests

Comme Webamp : tests C# (xUnit) du protocole (analyse des messages et des tags, CAP et SASL, reconnexion, commandes), des réglages et des journaux ; parcours Playwright dans l'hôte avec un faux serveur IRC par WebSocket (se connecter, rejoindre un salon, recevoir une mention et sa notification, message privé, reconnexion) ; un essai par le relais quand un serveur Kazo est présent.

## Licences

- Ce dépôt : MIT.
- Aucun code de KiwiIRC n'est repris ; s'il fallait reprendre une icône ou un
  autre fichier tiers, il serait noté dans `THIRD-PARTY-NOTICES.md`.

## Plus tard

- Serveur IRC simulé : [#1](https://github.com/Kazo-ca/Os-App-IRC/issues/1) (après [Os#393](https://github.com/Kazo-ca/Os/issues/393)).
- Liens `irc://` et `ircs://` : [#2](https://github.com/Kazo-ca/Os-App-IRC/issues/2) (après [Os#394](https://github.com/Kazo-ca/Os/issues/394)).
- DCC, aperçu des images, autres mécanismes SASL.

## Avancement

Le 2026-11-07, Benoit a lancé les chantiers. Série `irc/base` (créée depuis
`main`), une PR par chantier vers `irc/base`, puis une PR `irc/base` → `main`
que Benoit fusionne. Côté Kazo OS, série `real-network/base` (ADR 0235).

| Chantier | Branche | Dépend de | État |
| --- | --- | --- | --- |
| Kazo OS 1 : `KazoClientWebSocket` | `real-network/1-websocket` (Kazo-ca/Os) | rien | Fait (Kazo OS 3.2.0) |
| Kazo OS 2 : TLS par le relais | `real-network/2-relay-tls` (Kazo-ca/Os) | rien | Fait (Kazo OS 3.2.0) |
| Kazo OS 3 : ports déclarés, `BlockedPorts` | `real-network/3-declared-ports` (Kazo-ca/Os) | rien | Fait (Kazo OS 3.2.0) |
| 1. Solution, manifeste, fenêtre, mise en page ordinateur et mobile | `irc/1-solution` | rien | Fait ([#5](https://github.com/Kazo-ca/Os-App-IRC/pull/5)) |
| 2. Protocole IRC, IRCv3, faux transport | `irc/2-protocol` | rien | Fait ([#4](https://github.com/Kazo-ca/Os-App-IRC/pull/4)) |
| 3. Connexion WebSocket et relais, réseaux, SASL | `irc/3-connect` | 1, 2, version de l'OS avec l'ADR 0235 | Fait ([#8](https://github.com/Kazo-ca/Os-App-IRC/pull/8)) |
| 4. Discussion branchée à l'interface (faux transport), notifications et journaux | `irc/4-notifications-and-logs` | 1, 2 | Fait ([#6](https://github.com/Kazo-ca/Os-App-IRC/pull/6)) |
| 5. Liens web | `irc/5-links` | 4 (affichage des messages) | Fait ([#7](https://github.com/Kazo-ca/Os-App-IRC/pull/7)) |
| 6. Fiche du Store, `v1.0.0` | `irc/6-store` | 3, 4, 5 | En revue |
