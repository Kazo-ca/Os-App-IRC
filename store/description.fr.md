# IRC

Un client de discussion IRC natif de Kazo OS, écrit en C# et Blazor, pour les **vrais réseaux IRC**,
sur ordinateur et sur téléphone. Il suit le thème de l'OS ; une police à chasse fixe est proposée
pour l'aspect « console ».

## Se connecter

- **Sans aucun serveur, par WebSocket.** Le réseau proposé est UnrealIRCd
  (`wss://irc.unrealircd.org/`). Tout réseau qui accepte IRC par WebSocket (IRCv3,
  `text.ircv3.net`) peut être ajouté par « Gérer les réseaux ».
- **Par le serveur Kazo, pour les autres réseaux.** Libera.Chat et OFTC n'acceptent pas WebSocket :
  IRC passe par le relais du serveur optionnel de Kazo OS, en TCP, avec TLS sur le port 6697 (6667
  en clair). Ces réseaux sont marqués « Serveur Kazo requis ». L'option est toujours proposée et
  essayée quand vous vous connectez ; sans serveur Kazo connecté, IRC le dit clairement (« Aucun
  serveur Kazo connecté »).
- Votre pseudo est votre nom d'utilisateur de Kazo OS ; il se change par réseau. Le mot de passe
  SASL d'un compte est gardé dans le trousseau de l'OS, jamais dans un fichier.
- Les réseaux et leurs salons rejoints automatiquement sont dans
  `~/.config/ca.kazo.irc/networks.json`.

## Approbation de l'administrateur

IRC demande l'accès au **vrai réseau** (`network.real.access`), une capacité que Kazo OS réserve à
l'administrateur. Au premier lancement, Kazo OS demande à un administrateur de l'approuver ; la
fenêtre d'approbation liste aussi les ports que l'app déclare pour le relais du serveur Kazo
(`tcp/6697` et `tcp/6667`).

## Discuter

- Trois colonnes sur ordinateur (réseaux et salons, messages, utilisateurs) ; une colonne avec des
  panneaux sur téléphone.
- Commandes `/join /part /msg /me /nick /topic /whois /away /quit /query /raw`, historique de la
  saisie (↑ et ↓) et complétion des pseudos (Tab).
- IRCv3 courant : CAP, SASL, server-time, message-tags, away-notify, multi-prefix, account-notify ;
  reconnexion automatique avec retour dans les salons.
- Une mention ou un message privé quand la fenêtre n'a pas le focus : notification de Kazo OS avec
  son, compteur dans le titre (« (2) IRC ») et pastilles dans la liste des salons.
- Les couleurs et le gras IRC sont dessinés ; une adresse web d'un message s'ouvre dans le
  Navigateur de Kazo OS.
- Une seule fenêtre ; elle reste connectée quand elle est réduite, et la fermer quitte les réseaux.

## Journaux

Les discussions sont écrites par jour, en texte, dans
`~/.local/share/ca.kazo.irc/logs/<réseau>/<salon>/AAAA-MM-JJ.log`, où les autres apps de Kazo OS
peuvent les lire. Le bouton « Journaux » de la barre d'outils les coupe.

## Limites de la première version

- Pas de transfert de fichiers (DCC) : il faudrait accepter des connexions entrantes.
- Les liens `irc://` ne s'ouvrent pas encore dans IRC (Kazo OS ne sait pas encore associer un schéma
  d'adresse à une app).
- Demande Kazo OS 3.2.0 ou plus récent.

## Licence

IRC est un logiciel libre sous licence MIT. Il n'embarque aucune œuvre tierce : le code, l'interface
et l'icône sont ceux du dépôt.
