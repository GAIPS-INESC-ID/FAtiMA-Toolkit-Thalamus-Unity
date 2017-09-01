using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using CookComputing.XmlRpc;
using UnityEngine;

public interface IFMLSpeech
{
    //The id is used in case you need to listen for the UtteranceStarted and UtteranceFinished events.
    //If you don't need it, id can be ""
    //The tagNames and TagValues should have the same dimension, 
    //and in case you have tags that should be replaced in the utterance (like 'playerName'), 
    //then the name and value should correspond to the same index in the arrays
    [XmlRpcMethod]
    void PerformUtterance(string id, string utterance, string category);
    [XmlRpcMethod]
    void PerformUtteranceWithTags(string id, string utterance, string[] tagNames, string[] tagValues);
    [XmlRpcMethod]
    void PerformUtteranceFromLibrary(string id, string category, string subcategory, string[] tagNames,
        string[] tagValues);
    [XmlRpcMethod]
    void CancelUtterance(string id);
}

public interface IFMLSpeechEvents
{
    [XmlRpcMethod]
    void UtteranceStarted(string id);
    [XmlRpcMethod]
    void UtteranceFinished(string id);
}

public interface ITUCEvents
{
    [XmlRpcMethod]
    void SentFromUnityToThalamus(string something);
}

public interface ITUCActions
{
    [XmlRpcMethod]
    void SentFromThalamusToUnity();
}

public interface ISMessagesRpc : IFMLSpeech, IXmlRpcProxy { }

public class ThalamusListener : XmlRpcListenerService, IFMLSpeechEvents
{
    private SingleCharacterDemo SCD;
    public ThalamusListener(SingleCharacterDemo scd)
    {
        SCD = scd;
    }

    public void UtteranceFinished(string id)
    {
        Debug.Log("UtteranceFinished " + id);
    }

    public void UtteranceStarted(string id)
    {
        Debug.Log("UtteranceStarted " + id);
    }
}

public class ThalamusConnector : IFMLSpeech
{
    private string _remoteAddress = "localhost";

    private bool _printExceptions = true;
    public string RemoteAddress
    {
        get { return _remoteAddress; }
        set
        {
            _remoteAddress = value;
            _remoteUri = string.Format("http://{0}:{1}/", _remoteAddress, _remotePort);
            _rpcProxy.Url = _remoteUri;
        }
    }

    private int _remotePort = 7000;
    public int RemotePort
    {
        get { return _remotePort; }
        set
        {
            _remotePort = value;
            _remoteUri = string.Format("http://{0}:{1}/", _remoteAddress, _remotePort);
            _rpcProxy.Url = _remoteUri;
        }
    }

    private HttpListener _listener;
    private bool _serviceRunning;
    private int _localPort = 7001;
    private bool _shutdown;
    List<HttpListenerContext> _httpRequestsQueue = new List<HttpListenerContext>();
    private Thread _dispatcherThread;
    private Thread _messageDispatcherThread;


    ISMessagesRpc _rpcProxy;
    private string _remoteUri = "";
    SingleCharacterDemo SCD;

    public ThalamusConnector(SingleCharacterDemo scd)
    {
        SCD = scd;
        _remoteUri = String.Format("http://{0}:{1}/", _remoteAddress, _remotePort);
        Debug.Log("ThalamusSueca endpoint set to " + _remoteUri);
        _rpcProxy = XmlRpcProxyGen.Create<ISMessagesRpc>();
        _rpcProxy.Timeout = 1000;
        _rpcProxy.Url = _remoteUri;


        _dispatcherThread = new Thread(DispatcherThreadThalamus);
        _messageDispatcherThread = new Thread(MessageDispatcherThalamus);
        _dispatcherThread.Start();
        _messageDispatcherThread.Start();
    }

    #region rpc stuff

    public void Dispose()
    {
        _shutdown = true;

        try
        {
            if (_listener != null) _listener.Stop();
        }
        catch { }

        try
        {
            if (_dispatcherThread != null) _dispatcherThread.Join();
        }
        catch { }

        try
        {
            if (_messageDispatcherThread != null) _messageDispatcherThread.Join();
        }
        catch { }
    }

    public void DispatcherThreadThalamus()
    {
        while (!_serviceRunning)
        {
            try
            {
                Debug.Log("Attempt to start service on port '" + _localPort + "'");
                _listener = new HttpListener();
                _listener.Prefixes.Add(string.Format("http://*:{0}/", _localPort));
                _listener.Start();
                Debug.Log("XMLRPC Listening on " + string.Format("http://*:{0}/", _localPort));
                _serviceRunning = true;
            }
            catch (Exception e)
            {
                _localPort++;
                Debug.Log(e.Message);
                Debug.Log("Port unavaliable.");
                _serviceRunning = false;
            }
        }

        while (!_shutdown)
        {
            try
            {
                HttpListenerContext context = _listener.GetContext();
                lock (_httpRequestsQueue)
                {
                    _httpRequestsQueue.Add(context);
                }
            }
            catch (Exception e)
            {
                if (_printExceptions) Debug.Log("Exception: " + e);
                _serviceRunning = false;
                if (_listener != null)
                    _listener.Close();
            }
        }
        Debug.Log("Terminated DispatcherThreadThalamus");
        //_listener.Close();
    }

    public void MessageDispatcherThalamus()
    {
        while (!_shutdown)
        {
            bool performSleep = true;
            try
            {
                if (_httpRequestsQueue.Count > 0)
                {
                    performSleep = false;
                    List<HttpListenerContext> httpRequests;
                    lock (_httpRequestsQueue)
                    {
                        httpRequests = new List<HttpListenerContext>(_httpRequestsQueue);
                        _httpRequestsQueue.Clear();
                    }
                    foreach (HttpListenerContext r in httpRequests)
                    {
                        //ProcessRequest(r);
                        (new Thread(ProcessRequestThalamus)).Start(r);
                        performSleep = false;
                    }
                }
            }
            catch (Exception e)
            {
                if (_printExceptions) Debug.Log("Exception: " + e);
            }
            if (performSleep) Thread.Sleep(10);
        }
        Debug.Log("Terminated MessageDispatcherThalamus");
    }

    public void ProcessRequestThalamus(object oContext)
    {
        try
        {
            XmlRpcListenerService svc = new ThalamusListener(SCD);
            svc.ProcessRequest((HttpListenerContext)oContext);
        }
        catch (Exception e)
        {
            if (_printExceptions) Debug.Log("Exception: " + e);
        }

    }



    #endregion
       

    public void PerformUtterance(string id, string utterance, string category)
    {
        try
        {
            _rpcProxy.PerformUtterance(id, utterance, category);
        }
        catch (Exception e)
        {
            if (_printExceptions) Debug.Log("Exception: " + e.Message + (e.InnerException != null ? ": " + e.InnerException : ""));
        }
    }

    public void PerformUtteranceWithTags(string id, string utterance, string[] tagNames, string[] tagValues)
    {
        throw new NotImplementedException();
    }

    public void PerformUtteranceFromLibrary(string id, string category, string subcategory, string[] tagNames, string[] tagValues)
    {
        throw new NotImplementedException();
    }

    public void CancelUtterance(string id)
    {
        throw new NotImplementedException();
    }
}
