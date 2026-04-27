using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class SpeechRecognitionTest : MonoBehaviour
{
    [Header("GameObject Triggers")]
    public GameObject RXImages;        // Start when this is active
    public GameObject TrialResults;    // Stop when this is active

    [Header("Hugging Face")]
    [SerializeField] private string hfApiKey = "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";
    private const string ModelUrl = "https://router.huggingface.co/hf-inference/models/openai/whisper-large-v3";

    [Header("Recording Settings")]
    [SerializeField] private int chunkDurationSeconds = 10;
    [SerializeField] private int _recordingFrequency = 16000;

    private AudioClip clip;
    private bool recording;
    private string micDevice;
    private string savePath;

    private System.Collections.Generic.List<float> _allSamples = new();
    private int _lastMicPosition = 0;
    private int _recordingChannels = 1;

    private bool _hasStartedThisCycle = false;

    private void Update()
    {
        // ── TRIGGER LOGIC ──

        // Start Recording: RXImages is ON and we haven't started yet
        if (RXImages != null && RXImages.activeInHierarchy && !recording && !_hasStartedThisCycle)
        {
            _hasStartedThisCycle = true;
            OnTrialStartTrigger();
        }

        // Stop Recording: TrialResults is ON and we are currently recording
        if (TrialResults != null && TrialResults.activeInHierarchy && recording)
        {
            OnTrialEndTrigger();
        }

        // Reset the cycle: When both are hidden, allow a new recording to trigger later
        if (RXImages != null && !RXImages.activeInHierarchy && TrialResults != null && !TrialResults.activeInHierarchy)
        {
            _hasStartedThisCycle = false;
        }

        // ── BUFFER LOGIC ──
        if (!recording || clip == null) return;
        DrainMicBuffer(Microphone.GetPosition(micDevice));
    }

    private void OnTrialStartTrigger()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string folder = Path.Combine(projectRoot, "AudioRecording");
        Directory.CreateDirectory(folder);

        // Generating a timestamp name since trialID isn't passed via events anymore
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        savePath = Path.Combine(folder, $"Recording_{timestamp}.wav");

        Debug.Log($"[SpeechRecognition] Triggered Start — Saving to: {savePath}");
        StartRecording();
    }

    private void OnTrialEndTrigger()
    {
        Debug.Log("[SpeechRecognition] Triggered End — Stopping recording.");
        StopRecording();
    }

    // ── RECORDING CORE ──

    private void StartRecording()
    {
        if (recording) return;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[SpeechRecognition] No microphone found.");
            return;
        }

        // Default to first device, or look for Oculus specifically
        micDevice = Microphone.devices[0];
        foreach (var device in Microphone.devices)
        {
            if (device.Contains("Oculus")) micDevice = device;
        }

        _allSamples.Clear();
        _lastMicPosition = 0;
        StartCoroutine(StartMicRecording());
    }

    private IEnumerator StartMicRecording()
    {
        clip = Microphone.Start(micDevice, true, chunkDurationSeconds, _recordingFrequency);
        _recordingFrequency = clip.frequency;
        _recordingChannels = clip.channels;

        yield return new WaitUntil(() => Microphone.GetPosition(micDevice) > 0);

        recording = true;
        _lastMicPosition = 0;
        Debug.Log($"[SpeechRecognition] Recording started on: {micDevice}");
    }

    private void DrainMicBuffer(int currentPos)
    {
        if (currentPos == _lastMicPosition) return;
        int totalSamples = clip.samples * clip.channels;

        if (currentPos > _lastMicPosition)
        {
            float[] chunk = new float[currentPos - _lastMicPosition];
            clip.GetData(chunk, _lastMicPosition);
            _allSamples.AddRange(chunk);
        }
        else
        {
            float[] chunkA = new float[totalSamples - _lastMicPosition];
            clip.GetData(chunkA, _lastMicPosition);
            _allSamples.AddRange(chunkA);

            float[] chunkB = new float[currentPos];
            clip.GetData(chunkB, 0);
            _allSamples.AddRange(chunkB);
        }
        _lastMicPosition = currentPos;
    }

    private void StopRecording()
    {
        if (!recording) return;

        DrainMicBuffer(Microphone.GetPosition(micDevice));
        Microphone.End(micDevice);
        recording = false;

        if (_allSamples.Count == 0) return;

        float[] samples = _allSamples.ToArray();
        byte[] wavBytes = EncodeAsWAV(samples, _recordingFrequency, _recordingChannels);

        File.WriteAllBytes(savePath, wavBytes);
        StartCoroutine(SendRecording(wavBytes));
    }

    private byte[] EncodeAsWAV(float[] samples, int frequency, int channels)
    {
        using var ms = new MemoryStream(44 + samples.Length * 2);
        using var writer = new BinaryWriter(ms);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write(frequency);
        writer.Write(frequency * channels * 2);
        writer.Write((ushort)(channels * 2));
        writer.Write((ushort)16);
        writer.Write("data".ToCharArray());
        writer.Write(samples.Length * 2);

        foreach (var s in samples)
            writer.Write((short)(Mathf.Clamp(s, -1f, 1f) * short.MaxValue));

        return ms.ToArray();
    }

    private IEnumerator SendRecording(byte[] wavBytes)
    {
        using var request = new UnityWebRequest(ModelUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(wavBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Authorization", $"Bearer {hfApiKey}");
        request.SetRequestHeader("Content-Type", "audio/wav");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string transcription = JsonUtility.FromJson<WhisperResponse>(request.downloadHandler.text).text;
            WriteTranscriptionToFile(transcription);
        }
    }

    [System.Serializable] private class WhisperResponse { public string text; }

    private void WriteTranscriptionToFile(string content)
    {
        string txtPath = Path.ChangeExtension(savePath, ".txt");
        File.WriteAllText(txtPath, content);
        Debug.Log("[SpeechRecognition] Saved: " + txtPath);
    }
}