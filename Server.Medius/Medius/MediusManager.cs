﻿using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using RT.Common;
using RT.Models;
using Server.Common;
using Server.Database.Models;
using Server.Medius.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using IChannel = DotNetty.Transport.Channels.IChannel;

namespace Server.Medius
{
    public class MediusManager
    {
        static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance<MediusManager>();

        class QuickLookup
        {
            public Dictionary<int, ClientObject> AccountIdToClient = new Dictionary<int, ClientObject>();
            public Dictionary<string, ClientObject> AccountNameToClient = new Dictionary<string, ClientObject>();
            public Dictionary<string, ClientObject> AccessTokenToClient = new Dictionary<string, ClientObject>();
            public Dictionary<string, ClientObject> SessionKeyToClient = new Dictionary<string, ClientObject>();

            public Dictionary<int, AccountDTO> BuddyInvitationsToClient = new Dictionary<int, AccountDTO>();

            public Dictionary<string, DMEObject> AccessTokenToDmeClient = new Dictionary<string, DMEObject>();
            public Dictionary<string, DMEObject> SessionKeyToDmeClient = new Dictionary<string, DMEObject>();


            public Dictionary<int, Channel> ChannelIdToChannel = new Dictionary<int, Channel>();
            public Dictionary<string, Channel> ChanneNameToChannel = new Dictionary<string, Channel>();
            public Dictionary<int, Game> GameIdToGame = new Dictionary<int, Game>();

            public Dictionary<int, Clan> ClanIdToClan = new Dictionary<int, Clan>();
            public Dictionary<string, Clan> ClanNameToClan = new Dictionary<string, Clan>();
        }

        private Dictionary<string, int[]> _appIdGroups = new Dictionary<string, int[]>();
        private Dictionary<int, QuickLookup> _lookupsByAppId = new Dictionary<int, QuickLookup>();

        private List<MediusFile> _mediusFiles = new List<MediusFile>();
        private List<MediusFileMetaData> _mediusFilesToUpdateMetaData = new List<MediusFileMetaData>();

        private ConcurrentQueue<ClientObject> _addQueue = new ConcurrentQueue<ClientObject>();



        #region Clients
        public List<ClientObject> GetClients(int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            return _lookupsByAppId.Where(x => appIdsInGroup.Contains(x.Key)).SelectMany(x => x.Value.AccountIdToClient.Select(x => x.Value)).ToList();
        }

        public ClientObject GetClientByAccountId(int accountId, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    if (quickLookup.AccountIdToClient.TryGetValue(accountId, out var result))
                        return result;
                }
            }

            return null;
        }

        public ClientObject GetClientByAccountName(string accountName, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);
            accountName = accountName.ToLower();

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    if (quickLookup.AccountNameToClient.TryGetValue(accountName, out var result))
                        return result;
                }
            }

            return null;
        }

        public ClientObject GetClientByAccessToken(string accessToken, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    if (quickLookup.AccessTokenToClient.TryGetValue(accessToken, out var result))
                        return result;
                }
            }

            return null;
        }

        public ClientObject GetClientBySessionKey(string sessionKey, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    if (quickLookup.SessionKeyToDmeClient.TryGetValue(sessionKey, out var result))
                        return result;
                }
            }

            return null;
        }

        public DMEObject GetDmeByAccessToken(string accessToken, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    if (quickLookup.AccessTokenToDmeClient.TryGetValue(accessToken, out var result))
                        return result;
                }
            }

            return null;
        }

        public DMEObject GetDmeBySessionKey(string sessionKey, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    if (quickLookup.SessionKeyToDmeClient.TryGetValue(sessionKey, out var result))
                        return result;
                }
            }

            return null;
        }

        public void AddDmeClient(DMEObject dmeClient)
        {
            if (!dmeClient.IsLoggedIn)
                throw new InvalidOperationException($"Attempting to add DME client {dmeClient} to MediusManager but client has not yet logged in.");

            if (!_lookupsByAppId.TryGetValue(dmeClient.ApplicationId, out var quickLookup))
                _lookupsByAppId.Add(dmeClient.ApplicationId, quickLookup = new QuickLookup());


            try
            {
                quickLookup.AccessTokenToDmeClient.Add(dmeClient.Token, dmeClient);
                quickLookup.SessionKeyToDmeClient.Add(dmeClient.SessionKey, dmeClient);
            }
            catch (Exception e)
            {
                // clean up
                if (dmeClient != null)
                {
                    if (dmeClient.Token != null)
                        quickLookup.AccessTokenToDmeClient.Remove(dmeClient.Token);

                    if (dmeClient.SessionKey != null)
                        quickLookup.SessionKeyToDmeClient.Remove(dmeClient.SessionKey);
                }

                throw e;
            }
        }

        public void AddClient(ClientObject client)
        {
            if (!client.IsLoggedIn)
                throw new InvalidOperationException($"Attempting to add {client} to MediusManager but client has not yet logged in.");

            _addQueue.Enqueue(client);
        }

        #endregion

        #region Games

        public uint GetGameCount(int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);
            uint count = 0;

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    lock (quickLookup.GameIdToGame)
                    {
                        count += (uint)quickLookup.GameIdToGame.Count();
                    }
                }
            }

            return count;
        }

        public Game GetGameByDmeWorldId(string dmeSessionKey, int dmeWorldId)
        {
            foreach (var lookupByAppId in _lookupsByAppId)
            {
                lock (lookupByAppId.Value.GameIdToGame)
                {
                    var game = lookupByAppId.Value.GameIdToGame.FirstOrDefault(x => x.Value?.DMEServer?.SessionKey == dmeSessionKey && x.Value?.DMEWorldId == dmeWorldId).Value;
                    if (game != null)
                        return game;
                }
            }

            return null;
        }

        public Channel GetWorldByName(string worldName)
        {
            foreach (var lookupByAppId in _lookupsByAppId)
            {
                lock (lookupByAppId.Value.ChanneNameToChannel)
                {
                    if (lookupByAppId.Value.ChanneNameToChannel.TryGetValue(worldName, out var channel))
                        return channel;
                }
            }

            return null;
        }

        public Game GetGameByGameId(int gameId)
        {
            foreach (var lookupByAppId in _lookupsByAppId)
            {
                lock (lookupByAppId.Value.GameIdToGame)
                {
                    if (lookupByAppId.Value.GameIdToGame.TryGetValue(gameId, out var game))
                        return game;
                }
            }

            return null;
        }

        /*
        
        public Game GetGameByGameId(ClientObject client, int gameId)
        {
            foreach (var lookupByAppId in _lookupsByAppId)
            {
                if (client.ApplicationId == 20764 || client.ApplicationId == 20364)
                {
                    lock (lookupByAppId.Value.GameIdToGame)
                    {
                        if (lookupByAppId.Value.GameIdToGame.TryGetValue(gameId, out var game))
                            Logger.Warn($"GameIdToGame {game.Id}");
                        return game;
                    }
                } else if (client.ApplicationId == lookupByAppId.Key) {
                    lock (lookupByAppId.Value.GameIdToGame)
                    {

                        if (lookupByAppId.Value.GameIdToGame.TryGetValue(gameId, out var game))
                            Logger.Warn($"GameIdToGame {game.Id}");
                        return game;
                    }
                }

            }

            return null;
        }
        */
        public async Task AddGame(Game game)
        {
            if (!_lookupsByAppId.TryGetValue(game.ApplicationId, out var quickLookup))
                _lookupsByAppId.Add(game.ApplicationId, quickLookup = new QuickLookup());

            quickLookup.GameIdToGame.Add(game.Id, game);
            await Program.Database.CreateGame(game.ToGameDTO());
        }

        public int GetGameCountAppId(int appId)
        {
            if (!_lookupsByAppId.TryGetValue(appId, out var quickLookup))
                _lookupsByAppId.Add(appId, quickLookup = new QuickLookup());

            int gameCount = quickLookup.GameIdToGame.Count;

            return gameCount;
        }

        public IEnumerable<Game> GetGameList(int appId, int pageIndex, int pageSize, IEnumerable<GameListFilter> filters)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            return _lookupsByAppId.Where(x => appIdsInGroup.Contains(x.Key))
                            .SelectMany(x => x.Value.GameIdToGame.Select(x => x.Value))
                            .Where(x => (x.WorldStatus == MediusWorldStatus.WorldActive || x.WorldStatus == MediusWorldStatus.WorldStaging) &&
                                        (filters.Count() == 0 || filters.Any(y => y.IsMatch(x))))
                            .Skip((pageIndex - 1) * pageSize)
                            .Take(pageSize);
        }

        public IEnumerable<Game> GetGameListAppId(int appId, int pageIndex, int pageSize)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            return _lookupsByAppId.Where(x => appIdsInGroup.Contains(x.Key))
                            .SelectMany(x => x.Value.GameIdToGame.Select(x => x.Value))
                            .Skip((pageIndex - 1) * pageSize)
                            .Take(pageSize);
        }

        #region CreateGame
        public async Task CreateGame(ClientObject client, IMediusRequest request)
        {
            if (!_lookupsByAppId.TryGetValue(client.ApplicationId, out var quickLookup))
                _lookupsByAppId.Add(client.ApplicationId, quickLookup = new QuickLookup());

            var appIdsInGroup = GetAppIdsInGroup(client.ApplicationId);
            string gameName = null;
            if (request is MediusCreateGameRequest r)
                gameName = r.GameName;
            else if (request is MediusCreateGameRequest1 r1)
                gameName = r1.GameName;

            var existingGames = _lookupsByAppId.Where(x => appIdsInGroup.Contains(client.ApplicationId)).SelectMany(x => x.Value.GameIdToGame.Select(g => g.Value));

            // Ensure the name is unique
            // If the host leaves then we unreserve the name
            if (existingGames.Any(x => x.WorldStatus != MediusWorldStatus.WorldClosed && x.WorldStatus != MediusWorldStatus.WorldInactive && x.GameName == gameName && x.Host != null && x.Host.IsConnected))
            {
                client.Queue(new RT_MSG_SERVER_APP()
                {
                    Message = new MediusCreateGameResponse()
                    {
                        MessageID = request.MessageID,
                        MediusWorldID = -1,
                        StatusCode = MediusCallbackStatus.MediusGameNameExists
                    }
                });
                return;
            }

            // Try to get next free dme server
            // If none exist, return error to clist
            var dme = Program.ProxyServer.GetFreeDme(client.ApplicationId);
            if (dme == null)
            {
                client.Queue(new MediusCreateGameResponse()
                {
                    MessageID = request.MessageID,
                    MediusWorldID = -1,
                    StatusCode = MediusCallbackStatus.MediusExceedsMaxWorlds
                });
                return;
            }

            // Create and add
            try
            {
                var game = new Game(client, request, client.CurrentChannel, dme);
                await AddGame(game);

                // Send create game request to dme server
                dme.Queue(new MediusServerCreateGameWithAttributesRequest()
                {
                    MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                    MediusWorldUID = (uint)game.Id,
                    Attributes = game.Attributes,
                    ApplicationID = client.ApplicationId,
                    MaxClients = game.MaxPlayers
                });
            }
            catch (Exception e)
            {
                // 
                Logger.Error(e);

                // Failure adding game for some reason
                client.Queue(new MediusCreateGameResponse()
                {
                    MessageID = request.MessageID,
                    MediusWorldID = -1,
                    StatusCode = MediusCallbackStatus.MediusFail
                });
            }
        }

        public async Task CreateGame1(ClientObject client, IMediusRequest request)
        {
            if (!_lookupsByAppId.TryGetValue(client.ApplicationId, out var quickLookup))
                _lookupsByAppId.Add(client.ApplicationId, quickLookup = new QuickLookup());

            var appIdsInGroup = GetAppIdsInGroup(client.ApplicationId);
            string gameName = null;
            if (request is MediusCreateGameRequest1 r)
                gameName = r.GameName;

            var existingGames = _lookupsByAppId.Where(x => appIdsInGroup.Contains(client.ApplicationId)).SelectMany(x => x.Value.GameIdToGame.Select(g => g.Value));

            // Ensure the name is unique
            // If the host leaves then we unreserve the name
            if (existingGames.Any(x => x.WorldStatus != MediusWorldStatus.WorldClosed && x.WorldStatus != MediusWorldStatus.WorldInactive && x.GameName == gameName && x.Host != null && x.Host.IsConnected))
            {
                client.Queue(new RT_MSG_SERVER_APP()
                {
                    Message = new MediusCreateGameResponse()
                    {
                        MessageID = request.MessageID,
                        MediusWorldID = -1,
                        StatusCode = MediusCallbackStatus.MediusGameNameExists
                    }
                });
                return;
            }

            // Try to get next free dme server
            // If none exist, return error to clist
            var dme = Program.ProxyServer.GetFreeDme(client.ApplicationId);
            if (dme == null)
            {
                client.Queue(new MediusCreateGameResponse()
                {
                    MessageID = request.MessageID,
                    MediusWorldID = -1,
                    StatusCode = MediusCallbackStatus.MediusExceedsMaxWorlds
                });
                return;
            }

            // Create and add
            try
            {
                var game = new Game(client, request, client.CurrentChannel, dme);
                await AddGame(game);

                // Send create game request to dme server
                dme.Queue(new MediusServerCreateGameWithAttributesRequest()
                {
                    MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                    MediusWorldUID = (uint)game.Id,
                    Attributes = game.Attributes,
                    ApplicationID = client.ApplicationId,
                    MaxClients = game.MaxPlayers
                });
            }
            catch (Exception e)
            {
                // 
                Logger.Error(e);

                // Failure adding game for some reason
                client.Queue(new MediusCreateGameResponse()
                {
                    MessageID = request.MessageID,
                    MediusWorldID = -1,
                    StatusCode = MediusCallbackStatus.MediusFail
                });
            }
        }

        #region Create Game P2P

        #region MediusServerCreateGameOnMeRequest / MediusServerCreateGameOnSelfRequest / MediusServerCreateGameOnSelfRequest0
        public async Task CreateGameP2P(ClientObject client, IMediusRequest request, IChannel channel, DMEObject dme)
        {
            if (!_lookupsByAppId.TryGetValue(client.ApplicationId, out var quickLookup))
                _lookupsByAppId.Add(client.ApplicationId, quickLookup = new QuickLookup());

            var appIdsInGroup = GetAppIdsInGroup(client.ApplicationId);
            string gameName = null;
            NetAddressList gameNetAddressList = null;

            var p2pHostAddress = (channel.RemoteAddress as IPEndPoint).Address.ToString();
            string p2pHostAddressRemoved = p2pHostAddress.Remove(0, 7);

            if (request is MediusServerCreateGameOnMeRequest r)
            {
                gameName = r.GameName;
                gameNetAddressList = r.AddressList;
            }
            else if (request is MediusServerCreateGameOnSelfRequest r1)
            {
                gameName = r1.GameName;
                gameNetAddressList = r1.AddressList;
            }
            else if (request is MediusServerCreateGameOnSelfRequest0 r2)
            {
                gameName = r2.GameName;
                gameNetAddressList = r2.AddressList;
            }

            var existingGames = _lookupsByAppId.Where(x => appIdsInGroup.Contains(client.ApplicationId)).SelectMany(x => x.Value.GameIdToGame.Select(g => g.Value));

            // Ensure the name is unique
            // If the host leaves then we unreserve the name
            if (existingGames.Any(x => x.WorldStatus != MediusWorldStatus.WorldClosed && x.WorldStatus != MediusWorldStatus.WorldInactive && x.GameName == gameName && x.Host != null && x.Host.IsConnected))
            {
                client.Queue(new RT_MSG_SERVER_APP()
                {
                    //Send Success response
                    Message = new MediusServerCreateGameOnMeResponse()
                    {
                        MessageID = request.MessageID,
                        Confirmation = MGCL_ERROR_CODE.MGCL_GAME_NAME_EXISTS,
                        MediusWorldID = -1,
                    }
                });
                return;
            }
            Logger.Debug("NON-DME SUPPORTED CLIENT**\n  NOT CHECKING FOR FREE DME SERVER");

            // Try to get next free dme server
            // If none exist, return error to clist
            //var dme = Program.ProxyServer.GetFreeDme(client.ApplicationId);
            //svar dme = Program.ProxyServer.();

            //dme.IP = p2pHostAddressRemoved.ToString();
            /*
            if (dme == null)
            {
                client.Queue(new MediusCreateGameResponse()
                {
                    MessageID = request.MessageID,
                    MediusWorldID = -1,
                    StatusCode = MediusCallbackStatus.MediusExceedsMaxWorlds
                });
                return;
            }
            */

            // Create and add
            try
            {
                var game = new Game(client, request, client.CurrentChannel, dme);

                //Set game host type to PeerToPeer for those speci
                game.GameHostType = MediusGameHostType.MediusGameHostPeerToPeer;

                // Join game
                await client.JoinGameP2P(game);
                await game.OnMediusServerCreateGameOnMeRequest(request);

                await AddGame(game);

                //Send Success response
                client.Queue(new MediusServerCreateGameOnMeResponse()
                {
                    MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                    Confirmation = MGCL_ERROR_CODE.MGCL_SUCCESS,
                    MediusWorldID = game.Id,
                });
            }
            catch (Exception e)
            {
                // 
                Logger.Error(e);

                // Failure adding game for some reason
                client.Queue(new MediusCreateGameResponse()
                {
                    MessageID = request.MessageID,
                    MediusWorldID = -1,
                    StatusCode = MediusCallbackStatus.MediusFail
                });
            }
        }
        #endregion

        #endregion

        #region JoinGameRequest
        public Task JoinGame(ClientObject client, MediusJoinGameRequest request)
        {
            #region Client
            /*
            if (client == null)
            {
                Logger.Warn($"Join Game Request Handler Error: Player is not priviliged [{client}]");
                client.Queue(new MediusJoinGameResponse()
                {
                    MessageID = request.MessageID,
                    StatusCode = MediusCallbackStatus.MediusGameNotFound
                });
            }
            */
            #endregion

            var game = GetGameByGameId(request.MediusWorldID); // MUM original fetches GameWorldData
            if (game == null)
            {
                Logger.Warn($"Join Game Request Handler Error: Error in retrieving game world info from MUM cache [{request.MediusWorldID}]");
                client.Queue(new MediusJoinGameResponse()
                {
                    MessageID = request.MessageID,
                    StatusCode = MediusCallbackStatus.MediusGameNotFound
                });
            }

            #region Password
            else if (game.GamePassword != null && game.GamePassword != string.Empty && game.GamePassword != request.GamePassword)
            {
                Logger.Warn($"Join Game Request Handler Error: This game's password {game.GamePassword} doesn't match the requested GamePassword {request.GamePassword}");
                client.Queue(new MediusJoinGameResponse()
                {
                    MessageID = request.MessageID,
                    StatusCode = MediusCallbackStatus.MediusInvalidPassword
                });
            }
            #endregion

            #region MaxPlayers
            else if (game.PlayerCount >= game.MaxPlayers)
            {
                Logger.Warn($"Join Game Request Handler Error: This game does not allow more than {game.MaxPlayers}. Current player count: {game.PlayerCount}");
                client.Queue(new MediusJoinGameResponse()
                {
                    MessageID = request.MessageID,
                    StatusCode = MediusCallbackStatus.MediusWorldIsFull
                });
            }
            #endregion

            #region GameHostType check
            else if (request.GameHostType != game.GameHostType)
            {
                Logger.Warn($"Join Game Request Handler Error: This games HostType {game.GameHostType} does not match the Requests HostType {request.GameHostType}");
                client.Queue(new MediusJoinGameResponse()
                {
                    MessageID = request.MessageID,
                    StatusCode = MediusCallbackStatus.MediusRequestDenied
                });
            }
            #endregion

            #region JoinType.MediusJoinAsMassSpectator
            else if (request.JoinType == MediusJoinType.MediusJoinAsMassSpectator && (Convert.ToInt32(game.Attributes) & 2) == 0)
            {
                Logger.Warn($"Join Game Request Handler Error: This game does not allow mass spectators. Attributes: {game.Attributes}");
            }
            #endregion

            else
            {
                //Program.AntiCheatPlugin.mc_anticheat_event_msg(AnticheatEventCode.anticheatJOINGAME, request.MediusWorldID, client.AccountId, Program.AntiCheatClient, request, 4);

                var dme = game.DMEServer;

                /*
                dme.Queue(new MediusServerJoinGameRequest()
                {
                    MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                    ConnectInfo = new NetConnectionInfo()
                    {
                        Type = NetConnectionType.NetConnectionTypePeerToPeerUDP,
                        WorldID = game.DMEWorldId,
                        SessionKey = client.SessionKey,
                        ServerKey = Program.GlobalAuthPublic,
                        AccessKey = client.Token,
                        AddressList = new NetAddressList()
                        {
                            AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                {
                                    new NetAddress() { Address = game.netAddressList.AddressList[1].Address, Port = game.netAddressList.AddressList[1].Port, AddressType = NetAddressType.NetAddressTypeExternal},
                                    new NetAddress() { AddressType = NetAddressType.NetAddressNone},
                                }
                        },
                    }
                });
                */


                // if This is a Peer to Peer Player Host as DME we treat differently
                if (game.GAME_HOST_TYPE == MGCL_GAME_HOST_TYPE.MGCLGameHostPeerToPeer 
                    && game.netAddressList.AddressList[0].AddressType == NetAddressType.NetAddressTypeSignalAddress)
                {
                    
                    game.Host.Queue(new MediusServerJoinGameRequest()
                    {
                        MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                        ConnectInfo = new NetConnectionInfo()
                        {
                            Type = NetConnectionType.NetConnectionTypePeerToPeerUDP,
                            AccessKey = client.Token,
                            SessionKey = client.SessionKey,
                            WorldID = game.DMEWorldId,
                            ServerKey = Program.GlobalAuthPublic,
                            AddressList = new NetAddressList()
                            {
                                AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                {
                                    new NetAddress() { Address = request.AddressList.AddressList[0].Address, Port = request.AddressList.AddressList[0].Port, AddressType = NetAddressType.NetAddressTypeSignalAddress},
                                    new NetAddress() { AddressType = NetAddressType.NetAddressNone},
                                }
                            },
                        }
                    });
                } 
                else if (game.GAME_HOST_TYPE == MGCL_GAME_HOST_TYPE.MGCLGameHostPeerToPeer
                        && game.netAddressList.AddressList[0].AddressType == NetAddressType.NetAddressTypeExternal
                        && game.netAddressList.AddressList[1].AddressType == NetAddressType.NetAddressTypeInternal)
                {

                    game.Host.Queue(new MediusServerJoinGameRequest()
                    {
                        MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                        ConnectInfo = new NetConnectionInfo()
                        {
                            Type = NetConnectionType.NetConnectionTypePeerToPeerUDP,
                            AccessKey = client.Token,
                            SessionKey = client.SessionKey,
                            WorldID = game.DMEWorldId,
                            ServerKey = Program.GlobalAuthPublic,
                            AddressList = new NetAddressList()
                            {
                                AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                {
                                    new NetAddress() { Address = request.AddressList.AddressList[0].Address, Port = request.AddressList.AddressList[0].Port, AddressType = NetAddressType.NetAddressTypeExternal},
                                    new NetAddress() { Address = request.AddressList.AddressList[1].Address, Port = request.AddressList.AddressList[1].Port, AddressType = NetAddressType.NetAddressTypeInternal},
                                }
                            },
                        }
                    });
                    
                }
                // Else send normal Connection type to DME
                else
                {
                    dme.Queue(new MediusServerJoinGameRequest()
                    {
                        MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                        ConnectInfo = new NetConnectionInfo()
                        {
                            Type = NetConnectionType.NetConnectionTypeClientServerTCPAuxUDP,
                            WorldID = game.DMEWorldId,
                            AccessKey = client.Token,
                            SessionKey = client.SessionKey,
                            ServerKey = Program.GlobalAuthPublic
                        }
                    });
                }
                
            }

            return Task.CompletedTask;
        }
        #endregion

        #region JoinGameRequest0
        public void JoinGame0(ClientObject client, MediusJoinGameRequest0 request)
        {
            var game = GetGameByGameId(request.MediusWorldID);
            if (game == null)
            {
                client.Queue(new MediusJoinGameResponse()
                {
                    MessageID = request.MessageID,
                    StatusCode = MediusCallbackStatus.MediusGameNotFound
                });
            }
            else if (game.GamePassword != null && game.GamePassword != string.Empty && game.GamePassword != request.GamePassword)
            {
                client.Queue(new MediusJoinGameResponse()
                {
                    MessageID = request.MessageID,
                    StatusCode = MediusCallbackStatus.MediusInvalidPassword
                });
            }
            else if (game.PlayerCount >= game.MaxPlayers)
            {
                client.Queue(new MediusJoinGameResponse()
                {
                    MessageID = request.MessageID,
                    StatusCode = MediusCallbackStatus.MediusWorldIsFull
                });
            }
            else
            {
                var dme = game.DMEServer;
                // if This is a Peer to Peer Player Host as DME we treat differently
                if (game.GameHostType == MediusGameHostType.MediusGameHostPeerToPeer)
                {
                    dme.Queue(new MediusServerJoinGameRequest()
                    {
                        MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                        ConnectInfo = new NetConnectionInfo()
                        {
                            Type = NetConnectionType.NetConnectionTypePeerToPeerUDP,
                            WorldID = game.DMEWorldId,
                            SessionKey = client.SessionKey,
                            ServerKey = Program.GlobalAuthPublic
                        }
                    });
                }
                // Else send normal Connection type
                else
                {
                    dme.Queue(new MediusServerJoinGameRequest()
                    {
                        MessageID = new MessageId($"{game.Id}-{client.AccountId}-{request.MessageID}"),
                        ConnectInfo = new NetConnectionInfo()
                        {
                            Type = NetConnectionType.NetConnectionTypeClientServerTCP,
                            WorldID = game.DMEWorldId,
                            SessionKey = client.SessionKey,
                            ServerKey = Program.GlobalAuthPublic
                        }
                    });
                }
            }
        }
        #endregion

        #endregion

        #region Channels

        public Channel GetChannelByChannelId(int channelId, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    lock (quickLookup.ChannelIdToChannel)
                    {
                        if (quickLookup.ChannelIdToChannel.TryGetValue(channelId, out var result))
                            return result;
                    }
                }
            }

            return null;
        }

        public Channel GetChannelByChannelName(string channelName, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    lock (quickLookup.ChannelIdToChannel)
                    {
                        return quickLookup.ChannelIdToChannel.FirstOrDefault(x => x.Value.Name == channelName && appIdsInGroup.Contains(x.Value.ApplicationId)).Value;
                    }
                }
            }

            return null;
        }

        public uint GetChannelCount(ChannelType type, int appId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);
            uint count = 0;

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    lock (quickLookup.ChannelIdToChannel)
                    {
                        count += (uint)quickLookup.ChannelIdToChannel.Count(x => x.Value.Type == type);
                    }
                }
            }

            return count;
        }

        public Channel GetOrCreateDefaultLobbyChannel(int appId)
        {
            Channel channel = null;
            var appIdsInGroup = GetAppIdsInGroup(appId);

            foreach (var appIdInGroup in appIdsInGroup)
            {
                if (_lookupsByAppId.TryGetValue(appIdInGroup, out var quickLookup))
                {
                    lock (quickLookup.ChannelIdToChannel)
                    {
                        //, x => x.Value.Type == ChannelType.Lobby
                        channel = quickLookup.ChannelIdToChannel.FirstOrDefault(x => x.Value.ApplicationId == appId).Value;
                        if (channel != null)
                            return channel;
                    }
                }
            }

            // create default
            channel = new Channel()
            {
                ApplicationId = appId,
                Name = "Default",
                Type = ChannelType.Lobby
            };
            _ = AddChannel(channel);

            return channel;
        }

        public async Task AddChannel(Channel channel)
        {
            if (!_lookupsByAppId.TryGetValue(channel.ApplicationId, out var quickLookup))
                _lookupsByAppId.Add(channel.ApplicationId, quickLookup = new QuickLookup());

            lock (quickLookup.ChannelIdToChannel)
            {
                quickLookup.ChannelIdToChannel.Add(channel.Id, channel);
            }

            lock (quickLookup.ChanneNameToChannel) {

                quickLookup.ChanneNameToChannel.Add(channel.Name, channel);
            }

            await channel.OnChannelCreate(channel);
        }

        public IEnumerable<Channel> GetChannelList(int appId, int pageIndex, int pageSize, ChannelType type)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            return _lookupsByAppId
                .Where(x => appIdsInGroup.Contains(x.Key))
                .SelectMany(x => x.Value.ChannelIdToChannel.Select(x => x.Value))
                .Where(x => x.Type == type)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize);
        }
        #endregion

        #endregion

        #region MFS
        public IEnumerable<MediusFile> GetFilesList(string path, string filenameBeginsWith, uint pageSize, uint startingEntryNumber)
        {
            lock (_mediusFiles)
            {

                string[] files = null;
                int counter = 0;

                if (filenameBeginsWith.ToString() == "*")
                {
                    files = Directory.GetFiles(path).Select(file => Path.GetFileName(file)).ToArray();
                }
                else
                {
                    files = Directory.GetFiles(path, Convert.ToString(filenameBeginsWith));
                }

                if (files.Length < pageSize)
                {
                    counter = files.Count();
                }
                else
                {
                    counter = (int)pageSize - 1;
                }

                for (int i = (int)startingEntryNumber - 1; i < counter; i++)
                {
                    string fileName = files[i];
                    FileInfo fi = new FileInfo(fileName);

                    try
                    {
                        _mediusFiles.Add(new MediusFile()
                        {
                            FileName = files[i],
                            FileID = (int)i,
                            FileSize = (int)fi.Length,
                            CreationTimeStamp = (int)Utils.ToUnixTime(fi.CreationTime),
                        });
                    } catch (Exception e)
                    {
                        Logger.Warn($"MFS FileList Exception:\n{e}");
                    }
                }
                return _mediusFiles;
            }
        }

        public IEnumerable<MediusFile> GetFilesListExt(string path, string filenameBeginsWith, uint pageSize, uint startingEntryNumber)
        {
            lock (_mediusFiles)
            {
                if (startingEntryNumber == 0)
                    return _mediusFiles;

                int counter = 0;
                string filenameBeginsWithAppended = filenameBeginsWith.Remove(filenameBeginsWith.Length - 1);

                string[] filesArray = Directory.GetFiles(path);

                //files = Directory.GetFiles(path).Select(file => Path.GetFileName(filenameBeginsWith)).ToArray();

                if (filesArray.Length < pageSize)
                {
                    counter = filesArray.Count() - 1;
                }
                else
                {
                    counter = (int)pageSize - 1;
                }

                for (int i = (int)(startingEntryNumber - 1); i < counter; i++)
                {
                    //string[] pathArray = path.TrimStart('[').TrimEnd(']').Split(',');

                    //string[] fileName = filesArray[i].Split(path, path.Length, options: StringSplitOptions.None);
                    //string FileNameAppended = UsingStringJoin(fileName);

                    try
                    {
                        string fileName = filesArray[i];
                        FileInfo fi = new FileInfo(fileName);

                        _mediusFiles.Where(x => x.FileName == fileName.StartsWith(filenameBeginsWithAppended).ToString()).ToList();

                        /*
                        _mediusFiles.Add(new MediusFile()
                        {
                            FileName = filenameBeginsWithAppended.ToString(),
                            FileID = (uint)i,
                            FileSize = (uint)fi.Length,
                            CreationTimeStamp = Utils.ToUnixTime(fi.CreationTime),
                        });
                        */
                    }
                    catch (Exception e)
                    {
                        Logger.Warn($"MFS FileListExt Exception:\n{e}");
                    }
                }
                return _mediusFiles;
            }
        }

        public IEnumerable<MediusFileMetaData> UpdateFileMetaData(string path, int appId, MediusFile mediusFile, MediusFileMetaData mediusFileMetaData)

        {
            lock (_mediusFilesToUpdateMetaData)
            {

                /*
                if (filename.ToString() != null)
                {
                    files = Directory.GetFiles(path).Select(file => Path.GetFileName(file)).ToArray();
                }
                else
                {
                    files = Directory.GetFiles(path, Convert.ToString(filename));
                }
                */
                try
                {
                    _mediusFilesToUpdateMetaData.Add(new MediusFileMetaData()
                    {
                        Key = mediusFileMetaData.Key,
                        Value = mediusFileMetaData.Value,
                    });
                }
                catch (Exception e)
                {
                    Logger.Warn($"MFS UpdateMetaData Exception:\n{e}");
                }

                return _mediusFilesToUpdateMetaData;
            }
        }
        #endregion

        #region Buddies

        public List<AccountDTO> AddToBuddyInvitations(int appId, AccountDTO accountToAdd)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            if (!_lookupsByAppId.TryGetValue(appId, out var quickLookup))
                _lookupsByAppId.Add(appId, quickLookup = new QuickLookup());

            lock (quickLookup.BuddyInvitationsToClient)
            {
                quickLookup.BuddyInvitationsToClient.Add(accountToAdd.AccountId, accountToAdd);
            }

            return _lookupsByAppId.Where(x => appIdsInGroup.Contains(x.Key))
                .SelectMany(x => x.Value.BuddyInvitationsToClient.Select(x => x.Value))
                .ToList();
        }

        public List<AccountDTO> GetBuddyInvitations(int appId, int AccountId)
        {
            var appIdsInGroup = GetAppIdsInGroup(appId);

            return _lookupsByAppId.Where(x => appIdsInGroup.Contains(x.Key))
                .SelectMany(x => x.Value.BuddyInvitationsToClient.Select(x => x.Value))
                .Where(x => x.AccountId == AccountId)
                .ToList();
        }

        #endregion

        #region Clans

        //public Clan GetClanByAccountId(int clanId, int appId)
        //{
        //    if (_clanIdToClan.TryGetValue(clanId, out var result))
        //        return result;

        //    return null;
        //}

        //public Clan GetClanByAccountName(string clanName, int appId)
        //{
        //    clanName = clanName.ToLower();
        //    if (_clanNameToClan.TryGetValue(clanName, out var result))
        //        return result;

        //    return null;
        //}

        //public void AddClan(Clan clan)
        //{
        //    if (!_lookupsByAppId.TryGetValue(clan.ApplicationId, out var quickLookup))
        //        _lookupsByAppId.Add(dmeClient.ApplicationId, quickLookup = new QuickLookup());

        //    _clanNameToClan.Add(clan.Name.ToLower(), clan);
        //    _clanIdToClan.Add(clan.Id, clan);
        //}

        #endregion

        #region Tick

        public async Task Tick()
        {
            await TickClients();

            await TickChannels();

            await TickGames();
        }

        private async Task TickChannels()
        {
            Queue<(QuickLookup, int)> channelsToRemove = new Queue<(QuickLookup, int)>();

            // Tick channels
            foreach (var quickLookup in _lookupsByAppId)
            {
                foreach (var channelKeyPair in quickLookup.Value.ChannelIdToChannel)
                {
                    if (channelKeyPair.Value.ReadyToDestroy)
                    {
                        Logger.Info($"Destroying Channel {channelKeyPair.Value}");
                        channelsToRemove.Enqueue((quickLookup.Value, channelKeyPair.Key));
                    }
                    else
                    {
                        await channelKeyPair.Value.Tick();
                    }
                }
            }

            // Remove channels
            while (channelsToRemove.TryDequeue(out var lookupAndChannelId))
                lookupAndChannelId.Item1.ChannelIdToChannel.Remove(lookupAndChannelId.Item2);
        }

        private async Task TickGames()
        {
            Queue<(QuickLookup, int)> gamesToRemove = new Queue<(QuickLookup, int)>();

            // Tick games
            foreach (var quickLookup in _lookupsByAppId)
            {
                foreach (var gameKeyPair in quickLookup.Value.GameIdToGame)
                {
                    if (gameKeyPair.Value.ReadyToDestroy)
                    {
                        Logger.Info($"Destroying Game {gameKeyPair.Value}");
                        await gameKeyPair.Value.EndGame();
                        gamesToRemove.Enqueue((quickLookup.Value, gameKeyPair.Key));
                    }
                    else
                    {
                        await gameKeyPair.Value.Tick();
                    }
                }
            }

            // Remove games
            while (gamesToRemove.TryDequeue(out var lookupAndGameId))
                lookupAndGameId.Item1.GameIdToGame.Remove(lookupAndGameId.Item2);
        }

        private async Task TickClients()
        {
            Queue<(int, string)> clientsToRemove = new Queue<(int, string)>();


            while (_addQueue.TryDequeue(out var newClient))
            {
                if (!_lookupsByAppId.TryGetValue(newClient.ApplicationId, out var quickLookup))
                    _lookupsByAppId.Add(newClient.ApplicationId, quickLookup = new QuickLookup());

                try
                {
                    quickLookup.AccountIdToClient.Add(newClient.AccountId, newClient);
                    quickLookup.AccountNameToClient.Add(newClient.AccountName.ToLower(), newClient);
                    quickLookup.AccessTokenToClient.Add(newClient.Token, newClient);
                    quickLookup.SessionKeyToClient.Add(newClient.SessionKey, newClient);
                }
                catch (Exception e)
                {
                    // clean up
                    if (newClient != null)
                    {
                        quickLookup.AccountIdToClient.Remove(newClient.AccountId);

                        if (newClient.AccountName != null)
                            quickLookup.AccountNameToClient.Remove(newClient.AccountName.ToLower());

                        if (newClient.Token != null)
                            quickLookup.AccessTokenToClient.Remove(newClient.Token);

                        if (newClient.SessionKey != null)
                            quickLookup.SessionKeyToClient.Remove(newClient.SessionKey);
                    }

                    Logger.Error(e);
                    //throw e;
                }
            }

            foreach (var quickLookup in _lookupsByAppId)
            {
                foreach (var clientKeyPair in quickLookup.Value.SessionKeyToClient)
                {
                    if (!clientKeyPair.Value.IsConnected)
                    {
                        if (clientKeyPair.Value.Timedout)
                            Logger.Warn($"Timing out client {clientKeyPair.Value}");
                        else
                            Logger.Info($"Destroying Client {clientKeyPair.Value}");

                        // Logout and end session
                        await clientKeyPair.Value.Logout();
                        clientKeyPair.Value.EndSession();

                        clientsToRemove.Enqueue((quickLookup.Key, clientKeyPair.Key));
                    }
                }
            }

            // Remove
            while (clientsToRemove.TryDequeue(out var appIdAndSessionKey))
            {
                if (_lookupsByAppId.TryGetValue(appIdAndSessionKey.Item1, out var quickLookup))
                {
                    if (quickLookup.SessionKeyToClient.Remove(appIdAndSessionKey.Item2, out var clientObject))
                    {
                        quickLookup.AccountIdToClient.Remove(clientObject.AccountId);
                        quickLookup.AccessTokenToClient.Remove(clientObject.Token);
                        quickLookup.AccountNameToClient.Remove(clientObject.AccountName.ToLower());
                    }
                }
            }

            /*
            try
            {

            } catch (Exception e)
            {
                Logger.Warn(e);
            }
            */
        }

        private void TickDme()
        {
            Queue<(int, string)> dmeToRemove = new Queue<(int, string)>();

            foreach (var quickLookup in _lookupsByAppId)
            {
                foreach (var dmeKeyPair in quickLookup.Value.SessionKeyToDmeClient)
                {
                    if (!dmeKeyPair.Value.IsConnected)
                    {
                        Logger.Info($"Destroying DME Client {dmeKeyPair.Value}");

                        // Logout and end session
                        dmeKeyPair.Value?.Logout();
                        dmeKeyPair.Value?.EndSession();

                        dmeToRemove.Enqueue((quickLookup.Key, dmeKeyPair.Key));
                    }
                }
            }

            // Remove
            while (dmeToRemove.TryDequeue(out var appIdAndSessionKey))
            {
                if (_lookupsByAppId.TryGetValue(appIdAndSessionKey.Item1, out var quickLookup))
                {
                    if (quickLookup.SessionKeyToDmeClient.Remove(appIdAndSessionKey.Item2, out var clientObject))
                    {
                        quickLookup.AccessTokenToDmeClient.Remove(clientObject.Token);
                    }
                }
            }
        }

        #endregion

        #region App Ids

        public async Task OnDatabaseAuthenticated()
        {
            // get supported app ids
            var appids = await Program.Database.GetAppIds();

            // build dictionary of app ids from response
            _appIdGroups = appids.ToDictionary(x => x.Name, x => x.AppIds.ToArray());
        }

        public bool IsAppIdSupported(int appId)
        {
            return _appIdGroups.Any(x => x.Value.Contains(appId));
        }

        public int[] GetAppIdsInGroup(int appId)
        {
            return _appIdGroups.FirstOrDefault(x => x.Value.Contains(appId)).Value ?? new int[0];
        }

        #endregion

        #region Misc

        public SECURITY_MODE GetServerSecurityMode(SECURITY_MODE securityMode, RSA_KEY rsaKey)
        {
            int result;

            result = (int)securityMode;

            if(securityMode == SECURITY_MODE.MODE_UNKNOWN)
            {
                //result = (KM_GetLocalPublicKey(RSA_KEY, 0x80000000, 0) != 0) + 1;

                //securityMode = (SECURITY_MODE)result;
            }


            return (SECURITY_MODE)result;
        }

        public void rt_msg_server_check_protocol_compatibility(int clientVersion, byte p_compatible)
        {



        }

        #region AnonymouseAccountIdGenerator
        /// <summary>
        /// Generates a Random Anonymous AccountID for MediusAnonymouseAccountRequest
        /// </summary>
        /// <param name="AnonymousIDRangeSeed">Config Value for changing the MAS</param>
        /// <returns></returns>
        public int AnonymousAccountIDGenerator(int AnonymousIDRangeSeed)
        {
            int result; // eax

            //for integers
            Random r = new Random();
            int rInt = r.Next(-80000000, 0);

            result = rInt;
            return result;
        }
        #endregion

        public string UsingStringJoin(string[] array)
        {
            return string.Join(string.Empty, array);
        }

        #endregion

        #region Vulgarity

        public void load_classifier(string filename)
        {
            int classifier = 0;
            var rootPath = Path.GetFullPath(Program.Settings.MediusVulgarityRootPath);

            try
            {
                var stream = File.Open(rootPath + filename, FileMode.OpenOrCreate, FileAccess.Read);

                FileInfo fi = new FileInfo(filename);

                if (fi.Extension == "cl")
                {
                    classifier = read_classifier(stream);
                }
                else
                {
                    Logger.Warn($"Unknown file type in {rootPath}.\n");
                }

            } catch (Exception ex) {

                Logger.Warn($"Cannot open {rootPath + "/" + filename}.\n");

            }

        }

        public int read_classifier(FileStream fileClassifier)
        {
            

            if(fileClassifier.Length == 20398493)
            {
                switch(fileClassifier.Length)
                {

                }
            }

            return 0;
        }
        #endregion

        #region DmeServerClient

        public Task DmeServerClientIpQuery(int WorldID, int TargetClient, IPAddress IP)
        {

            return Task.CompletedTask;
        }

        public DME_SERVER_RESULT DmeServerMapRtError(uint RtError)
        {
            DME_SERVER_RESULT result;
            if (RtError == 52518)
            {
                result = DME_SERVER_RESULT.DME_SERVER_UNKNOWN_MSG_TYPE;
                return result;
            }

            if (RtError > 0xCD26)
            {
                result = DME_SERVER_RESULT.DME_SERVER_MUTEX_ERROR;
                if (RtError != 52528)
                {
                    if (RtError > 0xCD30)
                    {
                        result = DME_SERVER_RESULT.DME_SERVER_UNSECURED_ERROR;
                        if (RtError != 52533)
                        {
                            if (RtError > 0xCD35)
                            {
                                result = DME_SERVER_RESULT.DME_SERVER_CONFIG_ERROR;
                                if (RtError != 52535)
                                {
                                    result = DME_SERVER_RESULT.DME_SERVER_BUFF_OVERFLOW_ERROR;
                                    if (RtError >= 0xCD37)
                                    {
                                        result = DME_SERVER_RESULT.DME_SERVER_PARTIAL_RW_ERROR;
                                        if (RtError != 52536)
                                        {
                                            result = DME_SERVER_RESULT.DME_SERVER_CLIENT_ALREADY_DISCONNECTED;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                //if (RtError == 52530)
                                    //return 30;
                                result = DME_SERVER_RESULT.DME_SERVER_NO_MORE_WORLDS;
                                if (RtError >= 0xCD32)
                                {
                                    result = DME_SERVER_RESULT.DME_SERVER_CLIENT_LIMIT;
                                    if (RtError != 52531)
                                    {
                                        result = DME_SERVER_RESULT.DME_SERVER_ENCRYPTED_ERROR;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        result = DME_SERVER_RESULT.DME_SERVER_MSG_TOO_BIG;
                        if (RtError != 52523)
                        {
                            if (RtError > 0xCD2B)
                            {
                                result = DME_SERVER_RESULT.DME_SERVER_PARTIAL_WRITE;
                                if (RtError != 52525)
                                {
                                    result = DME_SERVER_RESULT.DME_SERVER_UNKNOWN_MSG_TYPE;
                                    if (RtError >= 0xCD2D)
                                    {
                                        result = DME_SERVER_RESULT.DME_SERVER_SOCKET_RESET_ERROR;
                                        if (RtError != 52526)
                                        {
                                            result = DME_SERVER_RESULT.DME_SERVER_CIRC_BUF_ERROR;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                result = DME_SERVER_RESULT.DME_SERVER_TCP_GET_WORLD_INDEX;
                                if (RtError != 52520)
                                {
                                    result = DME_SERVER_RESULT.DME_SERVER_WOULD_BLOCK;
                                    if (RtError >= 0xCD28)
                                    {
                                        result = DME_SERVER_RESULT.DME_SERVER_READ_ERROR;
                                        if (RtError != 52521)
                                        {
                                            result = DME_SERVER_RESULT.DME_SERVER_SOCKET_CLOSED;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (RtError == 52508)
                {
                    result = DME_SERVER_RESULT.DME_SERVER_CONN_MSG_ERROR;
                    return result;
                }
                if (RtError > 0xCD1C)
                {
                    result = DME_SERVER_RESULT.DME_SERVER_SOCKET_BIND_ERROR;
                    if (RtError != 52513)
                    {
                        if (RtError > 0xCD21)
                        {
                            result = DME_SERVER_RESULT.DME_SERVER_SOCKET_LISTEN_ERROR;
                            if (RtError != 52515)
                            {
                                result = DME_SERVER_RESULT.DME_SERVER_SOCKET_POLL_ERROR;
                                if (RtError >= 0xCD23)
                                {
                                    result = DME_SERVER_RESULT.DME_SERVER_SOCKET_READ_ERROR;
                                    if (RtError != 52516)
                                    {
                                        result = DME_SERVER_RESULT.DME_SERVER_SOCKET_WRITE_ERROR;
                                    }
                                }
                            }
                        }
                        else
                        {
                            result = DME_SERVER_RESULT.DME_SERVER_STACK_LOAD_ERROR;
                            if (RtError != 52510)
                            {
                                result = DME_SERVER_RESULT.DME_SERVER_WORLD_FULL;
                                if (RtError >= 0xCD1E)
                                {
                                    result = DME_SERVER_RESULT.DME_SERVER_SOCKET_CREATE_ERROR;
                                    if (RtError != 52511)
                                    {
                                        result = DME_SERVER_RESULT.DME_SERVER_SOCKET_OPT_ERROR;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (RtError == 52503)
                    {
                        result = DME_SERVER_RESULT.DME_SERVER_NOT_INITIALIZED;
                        return result;
                    }
                        
                    if (RtError <= 0xCD17)
                    {
                        if (RtError == 52501)
                        {
                            result = DME_SERVER_RESULT.DME_SERVER_INVALID_PARAM;
                            return result;
                        }
                        if (RtError > 0xCD15)
                        {
                            result = DME_SERVER_RESULT.DME_SERVER_NOT_IMPLEMENTED;
                            return result;
                        }
                    }

                    result = DME_SERVER_RESULT.DME_SERVER_MEM_ALLOC;
                    if (RtError != 52505)
                    {
                        result = DME_SERVER_RESULT.DME_SERVER_UNKNOWN_RESULT;
                        if (RtError >= 0xCD19)
                        {
                            result = DME_SERVER_RESULT.DME_SERVER_SOCKET_LIMIT;
                            if (RtError != 52506)
                            {
                                result = DME_SERVER_RESULT.DME_SERVER_UNKNOWN_CONN_ERROR;
                            }
                        }
                    }
                }
            }

            return result;
        }

        #endregion

        #region Matchmaking
        public int CalculateSizeOfMatchRoster(MediusMatchRosterInfo roster)
        {
            int rosterSize;
            uint v3;
            uint partySize;

            MediusMatchPartyInfo mediusMatchPartyInfo = new MediusMatchPartyInfo();

            if (roster == null)
                return 0;
            rosterSize = 4 * roster.NumParties + 8;
            partySize = (uint)roster.Parties;
            v3 = (uint)(4 * roster.NumParties + partySize - 4);
            while (partySize <= v3)
            {
                rosterSize += CalculateSizeOfMatchParty(mediusMatchPartyInfo);
                partySize += 4;
            }

            return rosterSize;
        }

        public int CalculateSizeOfMatchParty(MediusMatchPartyInfo party)
        {
            int MatchPartySize;
            if (party != null)
            {
                MatchPartySize = 8 * party.NumPlayers + 8;
            }
            else
            {
                MatchPartySize = 0;
            }

            return MatchPartySize;
        }
        #endregion
    }
}