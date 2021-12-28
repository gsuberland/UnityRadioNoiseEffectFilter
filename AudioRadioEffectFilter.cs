using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Collections;

/*

Radio noise effect filter.
Emulates the radio noise you might hear if using a handheld radio over long distance.
Distance specified as 0.0f to 1.0f range, so you can drive it from any distance value.

Version 0.1
Created by Graham Sutherland
Released under MIT license.

*/

public class AudioRadioEffectFilter : MonoBehaviour, ISerializationCallbackReceiver
{
    [RangeAttribute(0.0f, 1.0f)]
    public float Distance = 0.0f;
    
	[RangeAttribute(0, 20000)]
	public int PreCutoffLow = 150;
	
	[RangeAttribute(0, 20000)]
	public int PreCutoffHigh = 3500;

	[RangeAttribute(0, 20000)]
	public int PostCutoffLow = 350;
	
	[RangeAttribute(0, 20000)]
	public int PostCutoffHigh = 5000;

    private List<IRadioFilter> filterChain;

    private RampWaveNoiseFilter rampNoiseFilter;
    private WhiteNoiseFilter preWhiteNoiseFilter;
    private WhiteNoiseFloorFilter noiseFloorFilter;
    private HighPassFilter preHighPassFilter;
    private LowPassFilter preLowPassFilter;
    private ModulateDemodulateFilter modemFilter;
    private PhaseAlternatingFilter phaseAlternatingFilter;
    private WaveshaperPoly4Filter waveshaperFilter;
    private WhiteNoiseFilter postWhiteNoiseFilter;
    private HighPassFilter postHighPassFilter;
    private LowPassFilter postLowPassFilter;
    private HighPassFilter postHighPassFilter2;
    private LowPassFilter postLowPassFilter2;
    private WhiteNoiseFloorFilter postNoiseFloorFilter;
    private RampWaveNoiseFilter rampNoiseFilter2;

    private float preGain = 1.0f;
    private float postGain = 1.0f;
    private int sampleRate;
    
    private bool initialised = false;

    // this is used to detect when the script is reloaded in the editor
    [NonSerialized]
    private object reloadSignal = null;

	void Start()
    {
        initialised = false;
        Init();
	}

    void Init()
    {
        if (initialised)
            return;
        
        this.sampleRate = AudioSettings.outputSampleRate; 

        filterChain = new List<IRadioFilter>();

        filterChain.Add(rampNoiseFilter = new RampWaveNoiseFilter(AudioSettings.outputSampleRate, 50.0f));
        filterChain.Add(preWhiteNoiseFilter = new WhiteNoiseFilter());
        filterChain.Add(noiseFloorFilter = new WhiteNoiseFloorFilter());
        filterChain.Add(preHighPassFilter = new HighPassFilter(AudioSettings.outputSampleRate));
        filterChain.Add(preLowPassFilter = new LowPassFilter(AudioSettings.outputSampleRate));
        filterChain.Add(modemFilter = new ModulateDemodulateFilter(AudioSettings.outputSampleRate));
        filterChain.Add(phaseAlternatingFilter = new PhaseAlternatingFilter());
        filterChain.Add(waveshaperFilter = new WaveshaperPoly4Filter());
        filterChain.Add(postWhiteNoiseFilter = new WhiteNoiseFilter());
        filterChain.Add(postHighPassFilter = new HighPassFilter(AudioSettings.outputSampleRate));
        filterChain.Add(postLowPassFilter = new LowPassFilter(AudioSettings.outputSampleRate));
        filterChain.Add(postHighPassFilter2 = new HighPassFilter(AudioSettings.outputSampleRate));
        filterChain.Add(postLowPassFilter2 = new LowPassFilter(AudioSettings.outputSampleRate));
        filterChain.Add(postNoiseFloorFilter = new WhiteNoiseFloorFilter());
        filterChain.Add(rampNoiseFilter2 = new RampWaveNoiseFilter(AudioSettings.outputSampleRate, 60.0f));

        reloadSignal = this;
        initialised = true;
    }

    void Reset()
    {
        initialised = false;

        rampNoiseFilter = null;
        preWhiteNoiseFilter = null;
        noiseFloorFilter = null;
        preHighPassFilter = null;
        preLowPassFilter = null;
        modemFilter = null;
        phaseAlternatingFilter = null;
        waveshaperFilter = null;
        postWhiteNoiseFilter = null;
        postHighPassFilter = null;
        postLowPassFilter = null;
        postHighPassFilter2 = null;
        postLowPassFilter2 = null;
        postNoiseFloorFilter = null;
        rampNoiseFilter2 = null;

        Init();
    }

    void Update()
    {
        Init();
        
		preHighPassFilter.CutoffFrequency = PreCutoffLow;
		preLowPassFilter.CutoffFrequency = PreCutoffHigh;
		postHighPassFilter.CutoffFrequency = PostCutoffLow;
		postLowPassFilter.CutoffFrequency = PostCutoffHigh;
		postHighPassFilter2.CutoffFrequency = (int)(PostCutoffLow * 1.01f);
		postLowPassFilter2.CutoffFrequency = (int)(PostCutoffHigh * 0.99f);
        postHighPassFilter2.Resonance = 0.35f;
        postLowPassFilter2.Resonance = 0.35f;
        
        foreach (var filter in filterChain)
            filter.Update();

        SetDistanceParameters();
    }


    // ensure that filter objects are recreated when 
    public void OnAfterDeserialize()
    {
        // this function is called when the script hot reloads AND when a property is modified in the editor.
        // when the script has been hot reloaded, reloadSignal is set to null because it is marked as NotSerialized.
        // however, when a property is modified, the object isn't fully recreated, so reloadSignal is not set to null.
        // this allows us to detect when the script was hot reloaded vs. when some other random deserialisation event occurs (e.g. property change)
        if (reloadSignal == null)
        {
            Debug.Log("Radio effect filter script was hot reloaded; re-initialising.");
            initialised = false;
        }
    }

    public void OnBeforeSerialize()
    {
        
    }

    public void OnBeforeDeserialize()
    {
        
    }

    void SetDistanceParameters()
    {
        float d = Mathf.Clamp01(this.Distance);

        // we set the waveshaper's gain levels in a 0.0-1.0 range based on some predetermined functions
        // the gain levels are independent of the gain scales, which are inherently larger for each index because of the effect of raising numbers in the -1.0 to 1.0 range to powers. generally the range goes up in decades.
        // gain1_level = sigmoid spanning 0.0 to 0.5 distance (by half max distance we don't want any of the original signal)
        // gain2_level = bell curve peaking around 0.38 and quickly dropping back to 0 by 0.7
        // gain3_level = bell curve with long tail, centered around 0.55. comes down to about 0.2 at the end.
        // gain4_level = x^2 rise with bell curve peaking around 0.8
        float gain1_level = 1.0f / (1.0f + Mathf.Exp(12.0f * d - 5.5f));
        float gain2_level = Mathf.Exp(12.0f * (d - 0.33f)) / Mathf.Pow(0.724f + Mathf.Exp(10.0f * (d - 0.42f)), 2.0f);
        float gain3_level = (Mathf.Exp(30.0f * (d - 0.615f)) / Mathf.Pow(0.08f + Mathf.Exp(18.0f * (d - 0.613f)), 2.0f)) + (0.1f * d) + (0.1f * d * d);
        float gain4_level = (Mathf.Exp(30.0f * (d - 0.8f)) / Mathf.Pow(0.49f + Mathf.Exp(30.0f * (d - 0.803f)), 2.0f)) + (0.7f * d * d) + (0.1f * Mathf.Pow(d, 10));

        // we set the ramp noise based on a sigmoid curve that goes from 0.0,0.0 to 1.0,1.0, i.e. at max distance we have max noise
        // again this is independent of the gain scale for that noise
        //float ramp_level = 1.0f / (1.0f + Mathf.Exp(-10.0f * d + 5.0f));
        float ramp_level = (1.08f * d) - (0.1f * Mathf.Sqrt(d)) + (0.03f * Mathf.Sin(40.3f * d));

        // similar sigmoid curve but for the phase alternating filter, but with a slight delay so it comes in later
        float phase_level = 1.0f / (1.0f + Mathf.Exp(-11.0f * d + 6.0f));

        // overall gain has a falloff too.
        // the pre-gain falloff is a mix of sigmoid and linear.
        float prg1 = 0.5f * (1.0f / (1.0f + Mathf.Exp(5.0f * d - 3.0f)));
        float prg2 = 0.5f * (1.0f - (0.92f * d));
        float prg3 = 0.015f * Mathf.Sin(62.0f * d * d);
        this.preGain = Mathf.Clamp01(prg1 + prg2 + prg3);
        // the post-gain is a bit more interesting. it actually amplifies as we get further away, with a big peak toward the end, in order to amplify the noise.
        float pog1 = Mathf.Exp(10.0f * (d - 0.692f)) / Mathf.Pow(1.0f + Mathf.Exp(20.0f * (d - 0.95f)), 2.0f);
        float pog2 = 1.2f - (0.8f * d);
        this.postGain = Mathf.Clamp(((pog1 + pog2) / 1.2f) + (0.85f * d), 0.0f, 2.5f);

        // mod/demod amount goes up in the second half of the range
        float md = (1.0f / (1.0f + Mathf.Exp(-50.0f * d + 18.0f))) - (0.5f * Mathf.Pow(d, 40.0f));
        modemFilter.Amount = md * 0.75f;

        // pre-applied multiplicative and additive noise scales linearly
        float preNoiseMul = d;
        float preNoiseAdd = d;

        // post-applied multiplicative and additive noise scales as a sigmoid ramp
        float postNoiseMul = 1.0f / (1.0f + Mathf.Exp(-10.2f * d + 5.0f));
        float postNoiseAdd = 1.0f / (1.0f + Mathf.Exp(-11.2f * d + 6.0f));

        // drop the high frequencies once we get beyond a certain distance (about 0.95)
        float maxFreq = 1.0f / (1.0f + Mathf.Exp(80.0f * d - 78.5f));
        preLowPassFilter.CutoffFrequency = (int)(this.PreCutoffHigh * maxFreq);
        postLowPassFilter.CutoffFrequency = (int)(this.PostCutoffHigh * maxFreq);

        // up the noise floor as we go. mostly linear, but with an exponential rise toward the end
        noiseFloorFilter.Amount = 0.05f + (d * 0.95f);
        postNoiseFloorFilter.Amount = 0.1f + (0.05f * d) + (0.4f * Mathf.Pow(d, 10.0f));

        // set white noise filter levels
        preWhiteNoiseFilter.MultiplyAmount = Mathf.Clamp01(preNoiseMul * 0.5f);
        preWhiteNoiseFilter.AddAmount = Mathf.Clamp01(preNoiseAdd * 0.15f);
        postWhiteNoiseFilter.MultiplyAmount = Mathf.Clamp01(postNoiseMul * 0.35f);
        postWhiteNoiseFilter.AddAmount = Mathf.Clamp01(postNoiseAdd * 0.15f);

        // set the waveshaper gains
        waveshaperFilter.Gain1 = Mathf.Clamp01(gain1_level);
        waveshaperFilter.Gain2 = Mathf.Clamp01(gain2_level) * 10.0f;
        waveshaperFilter.Gain3 = Mathf.Clamp01(gain3_level) * 100.0f;
        waveshaperFilter.Gain4 = Mathf.Clamp01(gain4_level) * 1000.0f;

        // set the ramp noise
        rampNoiseFilter.Amount = Mathf.Clamp01(ramp_level);
        rampNoiseFilter2.Amount = Mathf.Clamp01(ramp_level);

        // set the phase alternating filter noise level
        phaseAlternatingFilter.Amount = Mathf.Clamp01(phase_level) * 0.1f;
    }

    // this is where the processing actually happens
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!initialised)
            return;

        // apply pre-gain
        for (int i = 0; i < data.Length; i++)
            data[i] *= this.preGain;

        // apply all the filters in the chain
        foreach (var filter in filterChain)
            filter.Process(data, channels);
        
        // apply post-gain and clamp
        for (int i = 0; i < data.Length; i++)
        {
            float vg = data[i] * this.postGain;
            float vc = (vg < -1.0f) ? -1.0f : ((vg > 1.0f) ? 1.0f : vg); // clamp -1.0f to 1.0f
            data[i] = vc; 
        }
    }


    interface IRadioFilter
    {
        void Process(float[] data, int channels);
        void Update();
    }


    /*
    * Phase alternating filter. Inverts the signal phase every n/2 samples.
    */
    class PhaseAlternatingFilter : IRadioFilter
    {
        private const int SampleCount = 16;
        public float Amount = 0.01f;

        public void Process(float[] data, int channels)
        {
            int dataLen = data.Length / channels;
            for (int i = 0; i < dataLen; i++)
            {
                float sign = ((i % SampleCount) < (SampleCount / 2)) ? 1.0f : -1.0f;
                for (int c = 0; c < channels; c++)
                {
                    float wet = data[i * channels + c] * sign;
                    float dry = data[i * channels + c];
                    float v = (wet * Amount) + (dry * (1.0f - Amount));
                    float vc = (v < -1.0f) ? -1.0f : ((v > 1.0f) ? 1.0f : v); // clamp -1.0f to 1.0f
                    data[i * channels + c] = vc;
                }
            }
        }

        public void Update() { }
    }


    /*
    * White noise filter (multiplicative and additive)
    */
    class WhiteNoiseFilter : IRadioFilter
    {
        public float MultiplyAmount = 0.05f;
        public float AddAmount = 0.005f;
        private float sampleSum = 0.0f;
        private float level = 0.0f;
        private XorShiftRNG rng;
        private float[] rngBuffer = new float[1024];

        public WhiteNoiseFilter()
        {
            rng = new XorShiftRNG(XorShiftRNG.MakeSeed());
        }

        public void Process(float[] data, int channels)
        {
            if (data.Length * 2 > rngBuffer.Length)
            {
                rngBuffer = new float[data.Length * 2];
            }
            rng.NextFloats(rngBuffer, rngBuffer.Length);

            int randIdx = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float sample = data[i];
                sampleSum += sample * sample;
                if (i % 16 == 15)
                {
                    level = Mathf.Sqrt(sampleSum) * 1.4f;
                    sampleSum = 0.0f;
                }
                float rand1 = rngBuffer[randIdx++];
                float rand2 = rngBuffer[randIdx++];
                float v = (sample * (1.0f + (rand1 * this.MultiplyAmount))) + (rand2 * AddAmount * level);
                float vc = (v < -1.0f) ? -1.0f : ((v > 1.0f) ? 1.0f : v); // clamp -1.0f to 1.0f
                data[i] = vc;
            }
        }

        public void Update() { }
    }


    /*
    * Noise floor filter. Ensures a minimum level of noise.
    */
    class WhiteNoiseFloorFilter : IRadioFilter
    {
        public float Amount = 0.05f;
        private XorShiftRNG rng;
        private float[] rngBuffer = new float[256];

        public WhiteNoiseFloorFilter()
        {
            rng = new XorShiftRNG(XorShiftRNG.MakeSeed());
        }

        public void Process(float[] data, int channels)
        {
            if (data.Length / 2 + 1 > rngBuffer.Length)
            {
                rngBuffer = new float[data.Length + 1];
            }
            rng.NextFloats(rngBuffer, data.Length);

            int dataLen = data.Length / channels;
            float noise = 0;
            int randIdx = 0;
            for (int i = 0; i < dataLen; i++)
            {
                // make the noise a bit more coarse.
                if ((i % 4) == 0)
                    noise = rngBuffer[randIdx++] * this.Amount * 0.01f;
                for (int c = 0; c < channels; c++)
                {
                    float d = data[i * channels + c];
                    float absd = (d < 0.0f) ? -d : d;
                    float sign = (d < 0.0f) ? -1.0f : 1.0f;
                    float maxd = noise > absd ? noise : absd;
                    data[i * channels + c] = sign * maxd;
                }
            }
        }

        public void Update() { }
    }


    /*
    * Ramp wave noise
    */
    class RampWaveNoiseFilter : IRadioFilter
    {
        public float Amount = 0.1f;

        private float deltaPerSample;
        private float value;

        public RampWaveNoiseFilter(int sampleRate, float freq)
        {
            this.value = 0;

            float period = 1.0f / freq;
            float secondsPerSample = 1.0f / sampleRate;
            this.deltaPerSample = secondsPerSample / period;
        }

        public void Process(float[] data, int channels)
        {
            int dataLen = data.Length / channels;
            for (int i = 0; i < dataLen; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    data[i * channels + c] *= 1.0f - (this.value * this.Amount);
                }

                this.value += this.deltaPerSample;
                if (this.value > 1.0f)
                {
                    this.value -= 1.0f;
                }
            }
        }

        public void Update() { }
    }


    /*
    * Wave shaper filter (4th order polynomial)
    */
    class WaveshaperPoly4Filter : IRadioFilter
    {
        public float Gain1 = 0.5f;
        public float Gain2 = 1.0f;
        public float Gain3 = 1.0f;
        public float Gain4 = 1.0f;

        public void Process(float[] data, int channels)
        {
            for (int i = 0; i < data.Length; i++)
            {
                float x = data[i];
                float x2 = x * x;
                float x3 = x2 * x;
                float x4 = x3 * x;
                float y = (Gain1 * x) + (Gain2 * x2) + (Gain3 * x3) + (Gain4 * x4);
                float yc = (y < -1.0f) ? -1.0f : ((y > 1.0f) ? 1.0f : y); // clamp -1.0f to 1.0f
                data[i] = yc;
            }
        }

        public void Update() { }
    }


    /*
    * Modulation / demodulation filter
    */
    class ModulateDemodulateFilter : IRadioFilter
    {
        public float Amount = 0.01f;
        public float CarrierFreq = 24000.0f;
        private float lastCarrierFreq = 0.0f;
        private float[] buffer = null;
        private float[] sineTable = null;
        private const int SampleCount = 8;
        private float secondsPerSubSample;

        public ModulateDemodulateFilter(int sampleRate)
        {
            this.secondsPerSubSample = 1.0f / (sampleRate * SampleCount);
        }

        public void Process(float[] data, int channels)
        {
            if (buffer == null || buffer.Length < data.Length * SampleCount)
            {
                buffer = new float[data.Length * SampleCount];
                sineTable = new float[data.Length * SampleCount];
                BuildSineTable();
            }

            // modulate
            int c = 0;
            for (int i = 0; i < data.Length; i++)
            {
                for (int s = 0; s < SampleCount; s++)
                {
                    buffer[c] = data[i] * sineTable[c];
                    c++;
                }
            }

            // demodulate
            c = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float sum2 = 0;
                for (int s = 0; s < SampleCount; s++)
                {
                    float d = buffer[c] * sineTable[c];
                    sum2 += d * d;
                    c++;
                }
                float rms = sum2 / 1.4f; // Mathf.Sqrt(sum2);
                float rmsc = (rms < -1.0f) ? -1.0f : ((rms > 1.0f) ? 1.0f : rms); // clamp -1.0f to 1.0f
                data[i] = ((1.0f - Amount) * data[i]) + (Amount * rmsc);
            }
        }

        private void BuildSineTable()
        {
            if (sineTable == null)
                return;
            for (int c = 0; c < sineTable.Length; c++)
            {
                sineTable[c++] = Mathf.Sin(c * Mathf.PI * 2.0f * secondsPerSubSample * CarrierFreq);
            }
        }

        public void Update()
        {
            if (!Mathf.Approximately(lastCarrierFreq, CarrierFreq))
            {
                BuildSineTable();
                lastCarrierFreq = CarrierFreq;
            }
        }
    }


    /*
    * Low pass filter
    */
    class LowPassFilter : IRadioFilter
    {
        public int CutoffFrequency = 5000;
        public float Resonance = 0.5f;

        private struct FilterState
        {
            public float a1;
            public float a2;
            public float a3;
            public float b1;
            public float b2;
            public float in1;
            public float in2;
            public float out1;
            public float out2;
        }

        private FilterState[] channelState;
        private int sampleRate;

        public LowPassFilter(int sampleRate)
        {
            this.sampleRate = sampleRate;
            channelState = new FilterState[2];
            for (int c = 0; c < 2; c++)
            {
                channelState[c] = new FilterState();
            }
            UpdateState();
        }

        public void Update()
        {
            UpdateState();
        }

        private void UpdateState()
        {
            for (int c = 0; c < channelState.Length; c++)
            {
                float f = 1.0f / Mathf.Tan(Mathf.PI * this.CutoffFrequency / this.sampleRate);
                var cs = channelState[c];
                cs.a1 = 1.0f / (1.0f + this.Resonance * f + f * f);
                cs.a2 = 2.0f * cs.a1;
                cs.a3 = cs.a1;
                cs.b1 = 2.0f * (1.0f - f * f) * cs.a1;
                cs.b2 = (1.0f - this.Resonance * f + f * f) * cs.a1;
                channelState[c] = cs;
            }
        }

        public void Process(float[] data, int channels)
        {
            bool channelsAdded = false;
            if (channelState.Length < channels)
            {
                var newChannelState = new FilterState[channels];
                System.Array.Copy(channelState, newChannelState, channelState.Length);
                for (int c = channelState.Length; c < channels; c++)
                    channelState[c] = new FilterState();
                
                channelsAdded = true;
            }
            if (channelsAdded)
            {
                UpdateState();
            }
            
            int dataLen = data.Length / channels;
            for (int i = 0; i < dataLen; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int idx = i * channels + c;
                    float v = data[idx];
                    var cs = channelState[c];
                    data[idx] = cs.a1 * data[idx] + cs.a2 * cs.in1 + cs.a3 * cs.in2 - cs.b1 * cs.out1 - cs.b2 * cs.out2;
                    cs.in2 = cs.in1;
                    cs.in1 = v;
                    cs.out2 = cs.out1;
                    cs.out1 = data[idx];
                    channelState[c] = cs;
                }
            }
        }
    }


    /*
    * High pass filter
    */
    class HighPassFilter : IRadioFilter
    {
        public int CutoffFrequency = 3500;
        public float Resonance = 0.5f;

        private struct FilterState
        {
            public float a1;
            public float a2;
            public float a3;
            public float b1;
            public float b2;
            public float in1;
            public float in2;
            public float out1;
            public float out2;
        }

        private FilterState[] channelState;
        private int sampleRate;

        public HighPassFilter(int sampleRate)
        {
            this.sampleRate = sampleRate;
            channelState = new FilterState[2];
            for (int c = 0; c < 2; c++)
            {
                channelState[c] = new FilterState();
            }
            UpdateState();
        }

        public void Update()
        {
            UpdateState();
        }

        private void UpdateState()
        {
            for (int c = 0; c < channelState.Length; c++)
            {
                float f = Mathf.Tan(Mathf.PI * this.CutoffFrequency / this.sampleRate);
                var cs = channelState[c];
                cs.a1 = 1.0f / (1.0f + this.Resonance * f + f * f);
                cs.a2 = -2.0f * cs.a1;
                cs.a3 = cs.a1;
                cs.b1 = 2.0f * (f * f - 1.0f) * cs.a1;
                cs.b2 = (1.0f - this.Resonance * f + f * f) * cs.a1;
                channelState[c] = cs;
            }
        }

        public void Process(float[] data, int channels)
        {
            bool channelsAdded = false;
            if (channelState.Length < channels)
            {
                var newChannelState = new FilterState[channels];
                System.Array.Copy(channelState, newChannelState, channelState.Length);
                for (int c = channelState.Length; c < channels; c++)
                    channelState[c] = new FilterState();
                
                channelsAdded = true;
            }
            if (channelsAdded)
            {
                UpdateState();
            }
            
            int dataLen = data.Length / channels;
            for (int i = 0; i < dataLen; i++)
            {
                for (int c = 0; c < channels; c++)
                {
                    int idx = i * channels + c;
                    float v = data[idx];
                    var cs = channelState[c];
                    data[idx] = cs.a1 * data[idx] + cs.a2 * cs.in1 + cs.a3 * cs.in2 - cs.b1 * cs.out1 - cs.b2 * cs.out2;
                    cs.in2 = cs.in1;
                    cs.in1 = v;
                    cs.out2 = cs.out1;
                    cs.out1 = data[idx];
                    channelState[c] = cs;
                }
            }
        }
    }

    class XorShiftRNG
    {
        private ulong _x;
        private ulong _y;
        private static readonly System.Random _seedRng = new System.Random();
        private static readonly object _seedRngLock = new object();

        public XorShiftRNG(ulong seed)
        {
            _x = SplitMix64(ref seed);
            _y = SplitMix64(ref seed);
        }

        public static ulong MakeSeed()
        {
            byte[] seedBytes = new byte[8];
            lock (_seedRngLock)
            {
                _seedRng.NextBytes(seedBytes);
            }
            return System.BitConverter.ToUInt64(seedBytes, 0);
        }

        private static ulong SplitMix64(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15ul;
                ulong result = state;
                result = (result ^ (result >> 30)) * 0xBF58476D1CE4E5B9ul;
                result = (result ^ (result >> 27)) * 0x94D049BB133111EBul;
                return result ^ (result >> 31);
            }
        }

        public ulong NextUInt64()
        {
            unchecked
            {
                ulong t = _x;
                ulong s = _y;
                _x = s;
                t ^= t << 23;
                t ^= t >> 18;
                t ^= s ^ (s >> 5);
                _y = t;
                return t + s;
            }
        }

        public uint NextUInt32()
        {
            unchecked
            {
                ulong t = _x;
                ulong s = _y;
                _x = s;
                t ^= t << 23;
                t ^= t >> 18;
                t ^= s ^ (s >> 5);
                _y = t;
                return (uint)((t + s) & 0xFFFFFFFFul);
            }
        }

        public double NextDouble()
        {
            unchecked
            {
                ulong t = _x;
                ulong s = _y;
                _x = s;
                t ^= t << 23;
                t ^= t >> 18;
                t ^= s ^ (s >> 5);
                _y = t;
                return (double)((t + s) & 0x100000000ul) / (double)0x100000000ul;
            }
        }

        public float NextFloat()
        {
            unchecked
            {
                ulong t = _x;
                ulong s = _y;
                _x = s;
                t ^= t << 23;
                t ^= t >> 18;
                t ^= s ^ (s >> 5);
                _y = t;
                return (float)((t + s) & 0x100000000ul) / (float)0x100000000ul;
            }
        }

        public void NextFloats(NativeArray<float> array, int count)
        {
            unchecked
            {
                const float divisor = (float)0x100000000ul;
                for (int i = 0; i < count; i++)
                {
                    ulong t = _x;
                    ulong s = _y;
                    _x = s;
                    t ^= t << 23;
                    t ^= t >> 18;
                    t ^= s ^ (s >> 5);
                    _y = t;
                    array[i] = (float)((s + t) & 0x100000000ul) / divisor;
                }
            }
        }

        public void NextFloats(float[] array, int count, int offset = 0)
        {
            unchecked
            {
                const float divisor = (float)0x100000000ul;
                for (int i = 0; i < count; i++)
                {
                    ulong t = _x;
                    ulong s = _y;
                    _x = s;
                    t ^= t << 23;
                    t ^= t >> 18;
                    t ^= s ^ (s >> 5);
                    _y = t;
                    array[i + offset] = (float)((s + t) & 0x100000000ul) / divisor;
                }
            }
        }
    }
}
