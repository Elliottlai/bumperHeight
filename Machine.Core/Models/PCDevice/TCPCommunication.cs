using MenthaAssembly.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MenthaAssembly;
using System.Net;
using MenthaAssembly.Network.Messages;
using System.Runtime.CompilerServices;
using System.Threading;
using Newtonsoft.Json;
using static System.Net.WebRequestMethods;
using System.Reflection;
using System.Reflection.Emit;
using Machine.Core.Interfaces;
using System.Diagnostics;
using System.Collections.Concurrent;

//namespace Machine.Core
//{

//    using M = cMachineManager;
//    [Serializable]
//    public class SendeObject
//    {
//        public SendeObject(string uid, object[] Param, [CallerMemberName] string func = null)
//        {
//            Uid = uid;
//            function = func;
//            Params = Param;
//        }
//        public string Uid;
//        public string function;
//        public object[] Params;
//    }
//    [Serializable]
//    public class CommandObject
//    {
//        public string Func;
//        public object[] Param;
//    }

//    [JsonObject]
//    public class TCPComm
//    {
//        [JsonIgnore]
//        public static TcpServer Server;
//        [JsonIgnore]
//        public static ConcurrentDictionary<string, TcpClient> Client;
//        [JsonIgnore]
//        public static ConcurrentDictionary<string, int> Client_IP_PORT;
//        [JsonIgnore]
//        public static bool Client_Init = false;
//        [JsonProperty]
//        public static CommunicationType communicationType { get; private set; } = CommunicationType.Server;
//        [JsonProperty]
//        public static String IP { get; private set; } = "127.0.0.1";
//        public static int PORT { get; private set; } = 168;

//        [JsonIgnore]
//        static bool IsConnect = true;


//        static string IpRegistration = "IpRegistration";

//        static string ClientInit = "ClientInit";

//        ~TCPComm()
//        {

//        }

//        static int FreeTcpPort()
//        {
//            System.Net.Sockets.TcpListener l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
//            l.Start();
//            int port = ((IPEndPoint)l.LocalEndpoint).Port;
//            l.Stop();
//            return port;
//        }

//        public static void Start()
//        {
//            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());

//            IP = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToString();

//            Server = new TcpServer(new ServerMessageHandler());



//            if (M.config.registerIP != IP)
//                PORT = FreeTcpPort();
//            else
//                PORT = M.config.registerPort;

//            Server.Start(IP, PORT);

//            bool bConnect = false;

//            Stopwatch Time = new Stopwatch();
//            Time.Start();
//            //20秒內對同一IP註冊
//            Client = new ConcurrentDictionary<string, TcpClient>();
//            Client_IP_PORT = new ConcurrentDictionary<string, int>();

//            if (IP != M.config.registerIP) // 不認識的要做
//            {
//                do
//                {
//                    try
//                    {
//                        Console.WriteLine("start registerIP");
//                        TcpClient client = new TcpClient(new ClientMessageHandler());
//                        client.Connect(M.config.registerIP, M.config.registerPort);
//                        Console.WriteLine("connect OK");

//                        if (M.config.register == true)
//                        {
//                            CommandObject com = new CommandObject();
//                            com.Func = IpRegistration;
//                            com.Param = new object[2] { IP, PORT };
//                            Console.WriteLine("CommandObject OK");

//                            var t = Task.Run(async () => await client.Send(new SendSerializeObjectRequest(com)));
//                            t.Wait();

//                            Console.WriteLine("ClientSend OK");
//                        }
//                        Client.TryAdd(M.config.registerIP, client);
//                        Client_IP_PORT.TryAdd(M.config.registerIP, M.config.registerPort);

//                        bConnect = true;

//                        break;

//                    }
//                    catch (Exception ex)
//                    {
//                        Console.WriteLine(ex.Message);
//                        Thread.Sleep(1000);
//                    }
//                } while (Time.ElapsedMilliseconds < 20 * 1000);

//                if (!bConnect)
//                {
//                    throw new Exception($"registerIP : {M.config.registerIP}\\{M.config.registerPort} Connect time out 20s");
//                }

//            }



//        }
//        public static void ClitenSendReady()
//        {

//            foreach (var c in Client_IP_PORT)
//            {

//                do
//                {
//                    CommandObject com = new CommandObject();
//                    com.Func = TCPComm.ClientInit;
//                    com.Param = new object[2] { c.Key, c.Value };
//                    Console.WriteLine("CommandObject OK");

//                    var t = Task.Run(async () => await Client[c.Key].Send(new SendSerializeObjectRequest(com)));
//                    t.Wait();

//                    if (t.Result is SuccessMessage SMessage)
//                    {
//                        if (SMessage.Success == true)
//                            break;
//                        else
//                            Thread.Sleep(1000);

//                    }
//                }
//                while (true);
//            }
//        }


//        public static object Send(object obj, object[] Param, [CallerMemberName] string function = null)
//        {
//            return (object)Protect(() =>
//            {
//                IComponent Comp = obj as IComponent;
//                IObject_Net ip = obj as IObject_Net;

//                SendeObject b = new SendeObject(Comp.UID, Param, function);

//                var c = Task.Run(async () => await Client[ip.IP].Send(new SendSerializeObjectRequest(b)));
//                c.Wait();

//                Console.WriteLine($"Receive : {c.Result.GetType().Name}");



//                if (c.Result is SendSerializeObjectResponse SSOR)
//                    return SSOR.SerializeObject;
//                if (c.Result is SuccessMessage SMessage)
//                {
//                    if (SMessage.Success == true)
//                        return new object();
//                    else
//                        throw new Exception("ClitenSend SuccessMessage is false");
//                }
//                if (c.Result is ErrorMessage error)
//                    throw new Exception(error.Message);
//                throw new Exception("ClitenSend IMessage is unKnow");
//            });
//        }

//        private static object Lock = new object();

//        public static T Protect<T>(Func<T> Function)
//        {
//            try
//            {
//                /*while (Monitor.IsEntered(Lock))
//                    Thread.Sleep(100);*/

//                Monitor.Enter(Lock);


//                if (IsConnect)
//                    return Function();
//                else
//                    return default(T);
//            }
//            finally
//            {
//                Monitor.Exit(Lock);
//            }
//        }
  
        
//        //public class ServerMessageHandler : IMessageHandler
//        //{
//        //    public IMessage HandleMessage(IPEndPoint Address, IMessage Message)
//        //    {
//        //        if (Message is SendSerializeObjectRequest SerializeMessage)
//        //        {
//        //            if (SerializeMessage.SerializeObject is SendeObject Datas)
//        //            {

//        //                object obj = null;

//        //                if (M.DOutputs.ContainsKey(Datas.Uid))
//        //                {
//        //                    obj = M.DOutputs[Datas.Uid];
//        //                }
//        //                else if (M.DInputs.ContainsKey(Datas.Uid))
//        //                {
//        //                    obj = M.DInputs[Datas.Uid];
//        //                }
//        //                else if (M.Axises.ContainsKey(Datas.Uid))
//        //                    obj = M.Axises[Datas.Uid];
//        //                else if (M.Lights.ContainsKey(Datas.Uid))
//        //                    obj = M.Lights[Datas.Uid];
//        //                if (Datas.function == "GetEntityComponent_IP")
//        //                {
//        //                    Console.WriteLine();
//        //                    bool r = obj != null;
//        //                    return new SendSerializeObjectResponse(true, r);
//        //                }


//        //                if (obj != null)
//        //                {


//        //                    var type = obj.GetType();
//        //                    var Method = type.GetMethod(Datas.function);
//        //                    if (Method != null)
//        //                    {
//        //                        var returnobject = Method.Invoke(obj, Datas.Params);
//        //                        if (returnobject == null)
//        //                            return new SuccessMessage(true);

//        //                        return new SendSerializeObjectResponse(true, returnobject);
//        //                    }
//        //                    var Property = type.GetProperty(Datas.function);
//        //                    if (Property != null)
//        //                    {
//        //                        if (Datas.Params != null)
//        //                        {
//        //                            Property.SetValue(obj, Datas.Params[0]);
//        //                            return new SuccessMessage(true);
//        //                        }
//        //                        else
//        //                        {
//        //                            var value = Property.GetValue(obj);
//        //                            if (value == null)
//        //                                return new SendSerializeObjectResponse(true, null);

//        //                            return new SendSerializeObjectResponse(true, value);
//        //                        }
//        //                    }

//        //                }
//        //            }
//        //            else if (SerializeMessage.SerializeObject is CommandObject com)
//        //            {

//        //                Console.WriteLine($"com.Func = {com.Func}");
//        //                if (com.Func == IpRegistration)
//        //                {
//        //                    string client_ip = (string)com.Param[0];
//        //                    int client_port = (int)com.Param[1];

//        //                    if (IP == client_ip)
//        //                        return new SuccessMessage(true);

//        //                    if (Client_IP_PORT.ContainsKey(client_ip) && Client_IP_PORT[client_ip] != client_port)
//        //                    {

//        //                        Client.TryRemove(client_ip, out TcpClient c);
//        //                        Client_IP_PORT.TryRemove(client_ip, out int a);

//        //                    }
//        //                    if (!Client_IP_PORT.ContainsKey(client_ip))
//        //                    {
//        //                        TcpClient c = new TcpClient(new ClientMessageHandler());
//        //                        c.Connect(client_ip, client_port);
//        //                        Client.TryAdd(client_ip, c);
//        //                        Client_IP_PORT.TryAdd(client_ip, client_port);

//        //                        Console.WriteLine($"Add client IP:{client_ip},Port:{client_port}");
//        //                        if (IP == M.config.registerIP) // 不認識的要做
//        //                        {
//        //                            foreach (var ip in Client_IP_PORT)
//        //                            {
//        //                                CommandObject com1 = new CommandObject();
//        //                                com1.Func = IpRegistration;
//        //                                com1.Param = new object[2] { ip.Key, ip.Value };
//        //                                Task t = Client[ip.Key].Send(new SendSerializeObjectRequest(com1));
//        //                                t.Wait();
//        //                            }
//        //                        }
//        //                    }
//        //                    return new SuccessMessage(true);
//        //                }
//        //                else if (com.Func == ClientInit)
//        //                {
//        //                    /*string client_ip = (string)com.Param[0];
//        //                    int client_port = (int)com.Param[1];*/
//        //                    // Client_Init[client_ip] = true;
//        //                    if (Client_Init == true)
//        //                        return new SuccessMessage(true);
//        //                    else
//        //                        return new SuccessMessage(false);
//        //                }
//        //            }

//        //        }

//        //        if (Message is SendMessageRequest SendMessage)
//        //        {
//        //            string ReceiveStr = SendMessage.Message;

//        //            return new SuccessMessage(true);
//        //        }

//        //        return new SuccessMessage(false);
//        //    }
//        //}

//        //public class ClientMessageHandler : IMessageHandler
//        //{
//        //    public IMessage HandleMessage(IPEndPoint Address, IMessage Message)
//        //    {

//        //        if (Message is SendSerializeObjectRequest SerializeMessage)
//        //        {
//        //            if (SerializeMessage.SerializeObject is SendeObject Datas)
//        //            {

//        //                object obj = null;

//        //                if (M.DOutputs.ContainsKey(Datas.Uid))
//        //                {
//        //                    obj = M.DOutputs[Datas.Uid];
//        //                }
//        //                else if (M.DInputs.ContainsKey(Datas.Uid))
//        //                {
//        //                    obj = M.DInputs[Datas.Uid];
//        //                }
//        //                else if (M.Axises.ContainsKey(Datas.Uid))
//        //                    obj = M.Axises[Datas.Uid];
//        //                else if (M.Lights.ContainsKey(Datas.Uid))
//        //                    obj = M.Lights[Datas.Uid];
//        //                if (Datas.function == "GetEntityComponent_IP")
//        //                {
//        //                    Console.WriteLine();
//        //                    bool r = obj != null;
//        //                    return new SendSerializeObjectResponse(true, r);
//        //                }


//        //                if (obj != null)
//        //                {


//        //                    var type = obj.GetType();
//        //                    var Method = type.GetMethod(Datas.function);
//        //                    if (Method != null)
//        //                    {
//        //                        var returnobject = Method.Invoke(obj, Datas.Params);
//        //                        if (returnobject == null)
//        //                            return new SuccessMessage(true);

//        //                        return new SendSerializeObjectResponse(true, returnobject);
//        //                    }
//        //                    var Property = type.GetProperty(Datas.function);
//        //                    if (Property != null)
//        //                    {
//        //                        if (Datas.Params != null)
//        //                        {
//        //                            Property.SetValue(obj, Datas.Params[0]);
//        //                            return new SuccessMessage(true);
//        //                        }
//        //                        else
//        //                        {
//        //                            var value = Property.GetValue(obj);
//        //                            if (value == null)
//        //                                return new SendSerializeObjectResponse(true, null);

//        //                            return new SendSerializeObjectResponse(true, value);
//        //                        }

//        //                    }

//        //                }
//        //            }
//        //            else if (SerializeMessage.SerializeObject is CommandObject com)
//        //            {

//        //                Console.WriteLine($"{com.Func}");
//        //                if (com.Func == IpRegistration)
//        //                {
//        //                    string client_ip = (string)com.Param[0];
//        //                    int client_port = (int)com.Param[1];

//        //                    if (client_ip == IP && client_port == PORT)
//        //                        return new SuccessMessage(true);
//        //                    if (Client_IP_PORT.ContainsKey(client_ip) && Client_IP_PORT[client_ip] != client_port)
//        //                    {
//        //                        Client.TryRemove(client_ip, out TcpClient c);
//        //                        Client_IP_PORT.TryRemove(client_ip, out int a);

//        //                    }
//        //                    if (!Client_IP_PORT.ContainsKey(client_ip))
//        //                    {
//        //                        TcpClient c = new TcpClient(new ClientMessageHandler());
//        //                        c.Connect(client_ip, client_port);
//        //                        Client.TryAdd(client_ip, c);
//        //                        Client_IP_PORT.TryAdd(client_ip, client_port);

//        //                        Console.WriteLine($"Add client IP:{client_ip},Port:{client_port}");
//        //                        if (IP == M.config.registerIP) // 不認識的要做
//        //                        {
//        //                            foreach (var ip in Client_IP_PORT)
//        //                            {
//        //                                foreach (var ip1 in Client_IP_PORT)
//        //                                {
//        //                                    CommandObject com1 = new CommandObject();
//        //                                    com1.Func = IpRegistration;
//        //                                    com1.Param = new object[2] { ip1.Key, ip1.Value };
//        //                                    Task t = Client[ip.Key].Send(new SendSerializeObjectRequest(com1));
//        //                                    t.Wait();
//        //                                }
//        //                            }
//        //                        }
//        //                    }
//        //                    return new SuccessMessage(true);
//        //                }
//        //                else if (com.Func == ClientInit)
//        //                {
//        //                    string client_ip = (string)com.Param[0];
//        //                    int client_port = (int)com.Param[1];
//        //                    // Client_Init[client_ip] = true;
//        //                    if (Client_Init == true)
//        //                        return new SuccessMessage(true);
//        //                    else
//        //                        return new SuccessMessage(false);
//        //                }
//        //            }
//        //        }



//        //        if (Message is SendMessageRequest SendMessage)
//        //        {
//        //            return new SendMessageResponse("Client Received");
//        //        }

//        //        return null;
//        //    }
//        //}


//    }
//}
