using MenthaAssembly.Network;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using MenthaAssembly.Network.Messages;
using System.Diagnostics;
using System.Threading;
using Machine.Core.Interfaces;
using System.Reflection;
using MenthaAssembly;
using System.Collections.Specialized;
using Swordfish.NET.Collections;
using System.Net.Sockets;
using System.Collections;

namespace Machine.Core
{

    using M = cMachineManager;
    [Serializable]
    public class MachinePackage
    {
        public MachinePackage(string uid, object[] Param, [CallerMemberName] string func = null)
        {
            Identify = TCPComm.Config.ContactID + Guid.NewGuid();

            Uid = uid;
            function = func;
            Params = Param;
            Finish = false;
            Return = null;

        }
        public string Identify;
        public string IP;
        public string Uid;
        public string function;
        public object[] Params;
        public bool Finish;
        public IMessage Return;
    }
    [Serializable]
    public class CommandObject
    {
        public string Func;
        public object[] Param;
    }
    public class cCommConfig
    {
        public string ContactID { get; set; } = "Contact";
        public string ServerIP { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 168;
        public int Timeout_ms { get; set; } = 20000;
        public bool IsServer { get; set; } = true;
        public void Save(string FileName) => this.ToJsonFile(FileName);
    }

    public class TCPComm
    {

        public static TcpServer Server;
        public static MenthaAssembly.Network.TcpClient Client;
        public static bool Client_Init = false;

        public static CommunicationType communicationType { get; private set; } = CommunicationType.Server;

        public static String IP { get; private set; } = "127.0.0.1";
        public static int PORT { get; private set; } = 168;

        public static cCommConfig Config { get; private set; }

        static bool IsConnect = true;

        public delegate bool SearchObjEventHandler(string uid, string IP, out object obj);
        public static event SearchObjEventHandler SearchObj;

        static ConcurrentObservableDictionary<String, MachinePackage> PackageFlow_Request = new ConcurrentObservableDictionary<String, MachinePackage>(true);
        //     static ConcurrentObservableCollection<MachinePackage> PackageFlow_Reply = new ConcurrentObservableCollection<MachinePackage>();
        public static void CommStart(cCommConfig config)
        {


            Config = config;

            if (!Config.IsServer)
                communicationType = CommunicationType.Client;
            else
                communicationType = CommunicationType.Server;

            if (communicationType == CommunicationType.Client)
            {

                Console.WriteLine("start registerIP");

                Client = new MenthaAssembly.Network.TcpClient(new ClientMessageHandler());

                LoopConnect();

            }
            else
            {
                try
                {
                    PORT = config.ServerPort;

                    Server = new TcpServer(new ServerMessageHandler());

                    IP = Config.ServerIP;

                    Server.Start(IP, PORT);
                    Server.Disconnected += ServerDisconnected;

                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    throw ex;
                }
            }

        }

        private static void ServerDisconnected(object sender, IPEndPoint e)
        {
           // throw new Exception($"registerIP : {Config.ServerIP}\\{Config.ServerPort} ServerDisconnected");
        }

        private static void LoopConnect()
        {

            Stopwatch Time = new Stopwatch();
            Time.Start();
            do
            {
                try
                {
                    Client.Connect(Config.ServerIP, Config.ServerPort);
                    Console.WriteLine("connect OK");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    if (Time.ElapsedMilliseconds > Config.Timeout_ms)
                        throw new Exception($"registerIP : {Config.ServerIP}\\{Config.ServerPort} Connect time out {Config.Timeout_ms} ms");
                    Thread.Sleep(1000);
                }

            } while (Time.ElapsedMilliseconds < Config.Timeout_ms * 2);
        }

        private static void Client_Disconnected(object sender, IPEndPoint e)
        {
            LoopConnect();
        }

        private static object Lock = new object();

        public static T Protect<T>(Func<T> Function)
        {
            try
            {

                Monitor.Enter(Lock);

                if (IsConnect)
                    return Function();
                else
                    return default(T);
            }
            finally
            {
                Monitor.Exit(Lock);
            }


        }

        private static void Send(MachinePackage package, int Timeout_ms = -1)
        {

            if (Timeout_ms == -1)
                Timeout_ms = Config.Timeout_ms;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            do
            {
                try
                {
                    if (communicationType == CommunicationType.Client)
                    {
                        lock (Client)
                        {

                            Client.Send(new SendSerializeObjectRequest(package), Timeout_ms);

                        }
                        break;
                    }
                    else
                    {
                        lock (Server)
                        {
                            int index = -1;
                            for (int i = 0; i < Server.Clients.Count; i++)
                                if (Server.Clients[i].Address.MapToIPv4().ToString() == IPAddress.Parse(package.IP).MapToIPv4().ToString())
                                    index = i;

                            if (index != -1)
                            {

                                Server.Send(Server.Clients[index], new SendSerializeObjectRequest(package), Timeout_ms);

                                break;
                            }
                            else
                            {
                                if (sw.ElapsedMilliseconds > Config.Timeout_ms)
                                    throw new Exception($"registerIP : {Config.ServerIP}\\{Config.ServerPort} Connect time out {Timeout_ms} ms");
                                Thread.Sleep(100);
                            }
                        }

                    }
                }
                catch (SocketException ex)
                {
                    LoopConnect();
                }
            } while (sw.ElapsedMilliseconds < Timeout_ms * 2);


        }

        public static void Reply(MachinePackage package, object Message, int Timeout_ms = -1)
        {

            Debug.WriteLine($"Reply - {package.Uid} - {package.function} - start");
            package.Finish = true;
            package.Return = (IMessage)Message;

            Send(package, Timeout_ms);

            Debug.WriteLine($"Reply - {package.Uid} - {package.function} - end");
        }



        public static object Send(object obj, object[] Param, int Timeout_ms = -1, [CallerMemberName] string function = null)
        {

            if (Timeout_ms == -1)
                Timeout_ms = Config.Timeout_ms;

            if (communicationType == CommunicationType.Server &&  Server.Clients.Count == 0)
                return null;

            return (new Func<object>(() =>
          {
              IComponent Comp = obj as IComponent;

              IObject_Net ip = obj as IObject_Net;
              string IP = Config.ServerIP;
              if (ip != null)
              {
                  IP = ip.IP;
              }
              // MachinePackage b = new MachinePackage(Comp.UID, ip.IP, Param, function);

              IMessage c = null;// Task.Run(  () =>   Client.Send(new SendSerializeObjectRequest(b)));


              MachinePackage package = new MachinePackage(Comp.UID, Param, function);
              package.IP = IP;

              Debug.WriteLine($"Request - {Comp.UID} - {function} - start");

              TaskCompletionSource<bool> TaskToken = new TaskCompletionSource<bool>();
              CancellationTokenSource CancelToken = new CancellationTokenSource(Timeout_ms);
              CancelToken.Token.Register(() => TaskToken.TrySetResult(false), false);

              if (PackageFlow_Request.TryAdd(package.Identify, (k) => package) == false)
                  Debug.WriteLine($"=========================================================================Add Suc false");

              void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
              {
                  if (e.Action == NotifyCollectionChangedAction.Replace)
                  {
                      KeyValuePair<string, Machine.Core.MachinePackage> kp = (KeyValuePair<string, Machine.Core.MachinePackage>)e.NewItems[0];


                      if (e.NewItems.Count > 1)
                          Debug.WriteLine("===================================================e.NewItems.Count > 1 ");


                      MachinePackage O = kp.Value;// aa.Values.ToList()[0];

                      if (O.Identify.Equals(package.Identify) &&
                          O.Finish == true)
                      {


                          PackageFlow_Request.CollectionChanged -= OnCollectionChanged;
                          TaskToken.TrySetResult(true);



                      }
                  }

              }
              PackageFlow_Request.CollectionChanged += OnCollectionChanged;

              Send(package, Timeout_ms);

              Debug.WriteLine($"Request - {Comp.UID} -  {function}   - end");


              TaskToken.Task.Wait();

              bool Success = TaskToken.Task.Result;

              package = PackageFlow_Request[package.Identify];
              PackageFlow_Request.Remove(package.Identify);

              if (Success)
              {
                  if (package.Return is SendSerializeObjectResponse SSOR)
                  {



                      return SSOR.SerializeObject;


                  }
                  if (package.Return is SuccessMessage SMessage)
                  {
                      if (SMessage.Success == true)
                          return new object();
                      else
                          throw new Exception("ClitenSend SuccessMessage is false");
                  }
                  if (package.Return is ErrorMessage error)
                      throw new Exception(error.Message);
                  throw new Exception("ClitenSend IMessage is unKnow");

              }
              else
                  throw new Exception($" Send Time Out : {Comp.UID} -  {function} ");
              #region MyRegion

              //if (communicationType == CommunicationType.Client)
              //{

              //    Debug.WriteLine($"{Comp.UID} - {function} - start");
              //    c = Client.Send(new SendSerializeObjectRequest(b), Timeout_ms);

              //    Debug.WriteLine($"{Comp.UID} -  {function}   - end");
              //}
              //else
              //{
              //    int index = -1;
              //    Stopwatch sw = new Stopwatch();

              //    sw.Start();

              //    do
              //    {

              //        for (int i = 0; i < Server.Clients.Count; i++)
              //            if (Server.Clients[i].Address.MapToIPv4().ToString() == IPAddress.Parse(ip.IP).ToString())
              //                index = i;

              //        if (index == -1)
              //        {
              //            if (sw.ElapsedMilliseconds > Config.Timeout_ms)
              //                throw new Exception($"registerIP : {Config.ServerIP}\\{Config.ServerPort} Connect time out {Timeout_ms} ms");
              //            Thread.Sleep(1000);
              //        }
              //        else
              //            break;

              //    }
              //    while (sw.ElapsedMilliseconds < Timeout_ms * 2);
              //    Debug.WriteLine($"{Comp.UID} - {function} - start");


              //    TCPComm.PackageFlow_Request.Add(b.Identify, b);


              //    c = Server.Send(Server.Clients[index], new SendSerializeObjectRequest(b), Timeout_ms);

              //    // b State changed 1.






              //    //斷線
              //    //編碼解碼錯誤  Sender 收到
              //    //

              //    Debug.WriteLine($"{Comp.UID} - {function} - end");

              //}

              //Console.WriteLine($"Receive : {c.GetType().Name}");

              //if (c is SendSerializeObjectResponse SSOR)
              //    return SSOR.SerializeObject;

              //if (c is SuccessMessage SMessage)
              //{
              //    if (SMessage.Success == true)
              //        return new object();
              //    else
              //        throw new Exception("ClitenSend SuccessMessage is false");
              //}
              //if (c is ErrorMessage error)
              //    throw new Exception(error.Message);
              //throw new Exception("ClitenSend IMessage is unKnow");
              #endregion
          }))();
        }


        public static void ProcessMessage(IPEndPoint Address, IMessage Message)
        {



            if (Message is SendSerializeObjectRequest SerializeMessage)
            {

                if (SerializeMessage.SerializeObject is MachinePackage Datas)
                {
                    Datas.IP = IPAddress.Parse(Address.ToString()).MapToIPv4().ToString();
                    if (Datas.Finish)
                    {

                        if (PackageFlow_Request.TryGetValue(Datas.Identify, out MachinePackage package))
                        {

                            PackageFlow_Request[Datas.Identify] = Datas;

                            return;
                        }
                        else
                            return;
                    }
                    Task.Factory.StartNew((p) =>
                    {
                        MachinePackage data = (MachinePackage)p;

                        // ProcessFlow find senderobject
                        // Modify State.
                        // Override ProcessFlow's item.

                        object obj = null;

                        if (SearchObj != null)
                            if (SearchObj(data.Uid, data.IP, out obj) == false)
                            {
                                Reply(data, new Exception("TCPComm-SearchObj: {Datas.Uid} - noFind "));   // throw new Exception("TCPComm-SearchObj: {Datas.Uid} - noFind ");
                                return;
                            }

                        if (obj != null)
                        {
                            var type = obj.GetType();

                            Type[] ParaTs = data.Params?.Select(i => i.GetType()).ToArray();


                            var Method = type.GetMethod(data.function, ParaTs ?? new Type[0]);


                            if (Method != null)
                            {
                                Debug.WriteLine($"Process -{type.Name} {Method.ToString()} - start");



                                 var returnobject = Method.Invoke(obj, Datas.Params);

                                // var returnobject = Method.Invoke(obj, param);
                                if (returnobject is Task t)
                                {
                                    Debug.WriteLine($"Process - {type.Name} {Method.ToString()} - end");

                                    t.Wait();
                                    Reply(data, new SuccessMessage(true));                                    //return new SuccessMessage(true);
                                    return;
                                }


                                if (returnobject == null)
                                {
                                    Reply(data, new SuccessMessage(true));                               // return new SuccessMessage(true);
                                    Debug.WriteLine($"Process - {type.Name} {Method.ToString()} - end");
                                    return;
                                }

                                Reply(data, new SendSerializeObjectResponse(true, returnobject));// return new SendSerializeObjectResponse(true, returnobject);
                                Debug.WriteLine($"Process - {type.Name} {Method.ToString()} - end");
                                return;
                            }
                            var Property = type.GetProperty(data.function);
                            if (Property != null)
                            {
                                Debug.WriteLine($"{type.Name} {Property.ToString()} - start");
                                if (data.Params != null)
                                {
                                    Property.SetValue(obj, data.Params[0]);
                                    Debug.WriteLine($"{type.Name} {Property.ToString()} - end");
                                    Reply(data, new SuccessMessage(true));                               // return new SuccessMessage(true);
                                    return;
                                }
                                else
                                {
                                    var value = Property.GetValue(obj);
                                    Debug.WriteLine($"{type.Name} {Property.ToString()} - end");
                                    if (value == null)
                                    {
                                        Reply(data, new SendSerializeObjectResponse(true, null));
                                        return;
                                    }
                                    Reply(data, new SendSerializeObjectResponse(true, value));
                                    return;
                                }
                            }

                        }

                    }, Datas);
                    //return new SuccessMessage(true ;);
                    return;
                }
            }

            Debug.WriteLine($"IMassage : {Message} is not  SendSerializeObjectRequest ");

            return;// new SuccessMessage(false);

        }
    }

    public class ServerMessageHandler : IMessageHandler
    {
        public IMessage HandleMessage(IPEndPoint Address, IMessage Message)
        {

            TCPComm.ProcessMessage(Address, Message);
            return new SuccessMessage(true);
        }
    }
    public class ClientMessageHandler : IMessageHandler
    {
        public IMessage HandleMessage(IPEndPoint Address, IMessage Message)
        {

            TCPComm.ProcessMessage(Address, Message);
            return new SuccessMessage(true);
        }
    }


}

