using System;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.WebRTC;
using ArenaUnity;
using ArenaUnity.HybridRendering.Signaling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArenaUnity.HybridRendering
{
    [RequireComponent(typeof(ArenaClientScene))]
    public class ARENACloud : MonoBehaviour
    {
        private ISignaling signaler;
        private RTCConfiguration config = new RTCConfiguration{
                iceServers = new[] {
                    new RTCIceServer {
                        urls = new[] {"stun:stun.l.google.com:19302"}
                    }
                }
            };

        Dictionary<string, PeerConnection> clientPeerDict = new Dictionary<string, PeerConnection>();

        private void Awake()
        {
            WebRTC.Initialize();
        }

        private void OnDestroy()
        {
            WebRTC.Dispose();
            Debug.Log("Destroyed");
        }

        // Start is called before the first frame update
        private void Start()
        {
            SetupSignaling();
            Debug.Log("Started Server!");
        }

        // Update is called once per frame
        private void Update()
        {

        }

        private void SetupSignaling()
        {
            GameObject gobj = new GameObject("Arena MQTT Signaler");
            signaler = gobj.AddComponent(typeof(ARENAMQTTSignaling)) as ARENAMQTTSignaling;
            signaler.SetSyncContext(SynchronizationContext.Current);

            // signaler = new ARENAMQTTSignaling(SynchronizationContext.Current);
            signaler.OnStart += OnSignalerStart;
            signaler.OnClientConnect += GotClientConnect;
            signaler.OnClientDisconnect += GotClientDisconnect;
            signaler.OnOffer += GotOffer;
            signaler.OnAnswer += GotAnswer;
            signaler.OnIceCandidate += GotIceCandidate;
            signaler.OnRemoteObjectStatusUpdate += GotRemoteObjectStatusUpdate;
            signaler.OpenConnection();
        }

        private void OnSignalerStart(ISignaling signaler)
        {
            foreach (var aobj in FindObjectsOfType<ArenaObject>())
            {
                JToken data = JToken.Parse(aobj.jsonData);
                var remoteRenderToken = data["remote-render"];
                if (remoteRenderToken != null)
                {
                    bool remoteRendered = remoteRenderToken["enabled"].Value<bool>();
                    // aobj.gameObject.SetActive(remoteRendered);
                    aobj.gameObject.GetComponent<Renderer>().enabled = remoteRendered;
                }
                else if (aobj.gameObject.activeSelf)
                {
                    aobj.gameObject.SetActive(false);
                }
            }

            StartCoroutine(WebRTC.Update());
        }

        private PeerConnection CreatePeerConnection(string id)
        {
            var pc = new RTCPeerConnection(ref config);
            // pc.SetConfiguration(ref config);
            PeerConnection peer = new PeerConnection(pc, id, signaler);
            clientPeerDict.Add(id, peer);
            return peer;
        }

        private void GotClientConnect(ISignaling signaler, string id)
        {
            PeerConnection peer;
            if (!clientPeerDict.TryGetValue(id, out peer))
            {
                peer = CreatePeerConnection(id);
                Debug.Log($"[Connect] There are now {clientPeerDict.Count} clients connected.");

                StartCoroutine(peer.StartNegotiationCoroutine());
            }
            else
            {
                peer.pc.Close();
            }
        }

        private void GotClientDisconnect(ISignaling signaler, string id)
        {
            PeerConnection peer;
            if (clientPeerDict.TryGetValue(id, out peer))
            {
                clientPeerDict.Remove(id);
                peer.Dispose();
                Debug.Log($"[Disconnect] There are now {clientPeerDict.Count} clients connected.");
            }
            else
                Debug.LogError($"Peer {id} not found in dictionary.");
        }

        private void GotOffer(ISignaling signaler, SDPData offer)
        {
            // Debug.Log("got offer.");

            PeerConnection peer;
            if (clientPeerDict.TryGetValue(offer.id, out peer))
                StartCoroutine(peer.CreateAndSendAnswerCoroutine(offer));
            else
                Debug.LogError($"Peer {offer.id} not found in dictionary.");
        }

        private void GotAnswer(ISignaling signaler, SDPData answer)
        {
            // Debug.Log("got answer.");

            PeerConnection peer;
            if (clientPeerDict.TryGetValue(answer.id, out peer))
                StartCoroutine(peer.SetRemoteDescriptionCoroutine(RTCSdpType.Answer, answer));
            else
                Debug.LogError($"Peer {answer.id} not found in dictionary.");
        }

        private void GotIceCandidate(ISignaling signaler, CandidateData data)
        {
            PeerConnection peer;
            if (clientPeerDict.TryGetValue(data.id, out peer))
                peer.AddIceCandidate(data);
            else
                Debug.LogError($"Peer {data.id} not found in dictionary.");
        }

        private void GotRemoteObjectStatusUpdate(ISignaling signaler, string objectId, bool remoteRendered)
        {
            // ArenaClientScene.Instance.arenaObjs.
            Debug.Log($"[GotRemoteObjectStatusUpdate] {objectId}, {remoteRendered}");

            foreach (var aobj in FindObjectsOfType<ArenaObject>())
            {
                if (aobj.name != objectId)
                {
                    continue;
                }

                // aobj.gameObject.SetActive(remoteRendered);
                aobj.gameObject.GetComponent<Renderer>().enabled = remoteRendered;
            }
        }
    }
}
