using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Physical production voices, phase/pause/distance ownership and silent offline stereo/reverb/music captures.")]
        public IEnumerator CityProductionAudio()
        {
            Assert.That(AudioListener.volume,Is.Zero,"The ordinary test run must remain muted.");
            float listenerVolume=AudioListener.volume, captureDelta=Time.captureDeltaTime;
            bool listenerPause=AudioListener.pause, capturing=false;
            float? filmWeight=BegottenModeRamp.DebugWeightOverride;
            var muted=new Dictionary<AudioSource,bool>();
            CityGameRoot city=null;
            CityCanneryController cannery=null;
            CityPortController port=null;
            CityPortSound sound=null;
            Camera camera=null;
            PlayerCameraFollow follow=null;
            bool canneryEnabled=false,soundEnabled=false,followEnabled=false,musicEnabled=false,locationMusicEnabled=false;
            Vector3 cameraPosition=Vector3.zero,heroPosition=Vector3.zero;
            Quaternion cameraRotation=Quaternion.identity;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                BegottenModeRamp.DebugWeightOverride=0f;
                yield return SceneManager.LoadSceneAsync(SceneIds.City,LoadSceneMode.Single);
                float deadline=Time.realtimeSinceStartup+TimeoutSeconds;
                while(Time.realtimeSinceStartup<deadline)
                {
                    city=Object.FindAnyObjectByType<CityGameRoot>();
                    if(city!=null&&city.IsInitialized&&!CompositionDriver.IsComposing)break;
                    yield return null;
                }
                Assert.That(city!=null&&city.IsInitialized,Is.True);
                musicEnabled=city.Music.enabled;
                locationMusicEnabled=city.LocationMusic.enabled;
                cannery=city.Cannery;
                port=city.World.Root.GetComponentInChildren<CityPortController>();
                sound=port.GetComponentInChildren<CityPortSound>();
                Assert.That(cannery!=null&&sound!=null&&sound.IsInitialized,Is.True);
                cannery.AutoAdvance=false;
                port.AutoAdvance=false;
                cannery.ForcePresentation=port.ForcePresentation=true;
                city.Player.Motor.SetInputEnabled(false);
                heroPosition=city.Player.GameObject.transform.position;
                camera=Camera.main;
                Assert.That(camera,Is.Not.Null);
                Assert.That(camera.GetComponent<AudioListener>(),Is.Not.Null);
                cameraPosition=camera.transform.position; cameraRotation=camera.transform.rotation;
                follow=camera.GetComponent<PlayerCameraFollow>();
                followEnabled=follow!=null&&follow.enabled;
                if(follow!=null)follow.enabled=false;
                canneryEnabled=cannery.enabled; soundEnabled=sound.enabled;
                // Stop autonomous frame owners. Their public samplers keep the
                // real source/filter/mixer graph alive at one deterministic pose.
                cannery.enabled=false;
                sound.enabled=false;
                AssertProductionVoiceOwners(city,cannery,port,sound);
                var all=new[]{sound.EngineSource,sound.FirstCraneSource,sound.SecondCraneSource,
                    sound.TrolleySource,sound.ContactSource,cannery.TruckEngineSource,cannery.SeamerSource,
                    cannery.RetortSource,cannery.ReverseAlarmSource};
                foreach(AudioSource source in all)AssertProductionVoice(source, source == sound.EngineSource);

                // Exercise the actual pause gate once with the ordinary frame
                // owners, then return to controlled offline sampling.
                cannery.enabled=true; sound.enabled=true;
                using(GameTimeScaleRuntime.AcquirePause())
                {
                    cannery.AutoAdvance=true;
                    cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f));
                    cannery.AdvanceSounds(true);
                    yield return null;
                    yield return null;
                    Assert.That(cannery.SeamerSource.isPlaying,Is.False);
                    Assert.That(sound.IsPaused,Is.True);
                    cannery.AutoAdvance=false;
                }
                cannery.enabled=false; sound.enabled=false;

                Assert.That(AudioSettings.GetConfiguration().speakerMode,Is.EqualTo(AudioSpeakerMode.Stereo));
                AudioListener.pause=false;
                Time.captureDeltaTime=1f/30f;
                string folder=Path.Combine(Directory.GetCurrentDirectory(),"Captures","CityProductionAudio");
                Directory.CreateDirectory(folder);
                var report=new List<string>();
                int rate=AudioSettings.outputSampleRate;
                // The first offline session can yield no samples in Unity.
                // A successful Start is required before lifting the test mute;
                // mute is restored before every Stop, including failure cleanup.
                Assert.That(AudioRenderer.Start(),Is.True);
                capturing=true;
                AudioListener.volume=1f;
                yield return PumpProductionAudio(rate/5,null,muted,Array.Empty<AudioSource>(),null,30,false);
                AudioListener.volume=0f;
                AudioRenderer.Stop(); capturing=false;
                yield return null;
                Assert.That(AudioRenderer.Start(),Is.True);
                capturing=true;
                AudioListener.volume=1f;

                double craneTime=CityPortCycle.UnloadStartSeconds+16d;
                port.ApplyAt(craneTime,15f);
                Action craneTick=()=>sound.Advance(craneTime,.5f,true);
                craneTick();
                yield return CaptureProductionStereo(camera,sound.FirstCraneSource,craneTick,"port-crane",folder,muted,report);
                cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f));
                Action seamerTick=()=>cannery.AdvanceSounds(true);
                seamerTick();
                yield return CaptureProductionStereo(camera,cannery.SeamerSource,seamerTick,"factory-seamer",folder,muted,report);
                yield return CaptureProductionReverb(camera,cannery.SeamerSource,seamerTick,folder,muted,report);

                city.LocationMusic.enabled=false;
                city.Music.SetPlaybackSuppressed(false);
                city.Music.ResumeWithFadeIn(0f);
                city.Music.AdvanceFade(10f);
                city.Music.enabled=false;
                yield return CaptureProductionMusic(camera,city,cannery,seamerTick,folder,muted,report);
                File.WriteAllLines(Path.Combine(folder,"measurements.txt"),report);
                foreach(string line in report)TestContext.Out.WriteLine(line);
            }
            finally
            {
                AudioListener.volume=0f;
                if(capturing)AudioRenderer.Stop();
                foreach(var pair in muted)if(pair.Key!=null)pair.Key.mute=pair.Value;
                if(cannery!=null){cannery.AdvanceSounds(false);cannery.enabled=canneryEnabled;cannery.AutoAdvance=false;}
                if(sound!=null){sound.Advance(port.ElapsedSeconds,0,false);sound.enabled=soundEnabled;}
                if(city!=null&&city.IsInitialized)
                {
                    city.Player.Motor.Teleport(heroPosition);
                    city.Music.enabled=musicEnabled;
                    city.LocationMusic.enabled=locationMusicEnabled;
                }
                if(camera!=null)camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
                if(follow!=null)follow.enabled=followEnabled;
                BegottenModeRamp.DebugWeightOverride=filmWeight;
                Time.captureDeltaTime=captureDelta;
                AudioListener.pause=listenerPause;
                AudioListener.volume=listenerVolume;
            }
        }

        private static void AssertProductionVoice(AudioSource source, bool trawlerEngine)
        {
            Assert.That(source,Is.Not.Null);
            Assert.That(source.clip,Is.Not.Null,source.name);
            Assert.That(source.clip.channels,Is.EqualTo(1),source.name+" needs a mono emitter.");
            Assert.That(source.spatialBlend,Is.EqualTo(1f));
            Assert.That(source.panStereo,Is.Zero);
            Assert.That(source.spread,Is.Zero);
            Assert.That(source.dopplerLevel,Is.Zero);
            Assert.That(source.rolloffMode,Is.EqualTo(AudioRolloffMode.Linear));
            Assert.That(source.minDistance,Is.InRange(2f,3f));
            if(trawlerEngine)Assert.That(source.maxDistance,Is.EqualTo(48f));
            else Assert.That(source.maxDistance,Is.InRange(24f,32f));
            Assert.That(source.outputAudioMixerGroup,Is.SameAs(GameAudioMixer.SfxWorldGroup));
            AudioReverbFilter reverb=source.GetComponent<AudioReverbFilter>();
            Assert.That(reverb,Is.Not.Null,source.name+" owns its work-space reflections.");
            Assert.That(reverb.enabled,Is.True);
            Assert.That(reverb.dryLevel,Is.EqualTo(0f));
            Assert.That(reverb.decayTime,Is.InRange(.2f,1.1f));
            Assert.That(source.GetComponent<AudioLowPassFilter>().cutoffFrequency,Is.GreaterThanOrEqualTo(2800f));
        }

        private static void AssertProductionVoiceOwners(CityGameRoot city,CityCanneryController cannery,
            CityPortController port,CityPortSound sound)
        {
            port.ApplyAt(30d,15f); sound.Advance(30d,.5f,true);
            Vector3 prior=sound.EngineSource.transform.position;
            port.ApplyAt(40d,15f); sound.Advance(40d,.5f,true);
            Assert.That(Vector3.Distance(prior,sound.EngineSource.transform.position),Is.GreaterThan(1f));
            Assert.That(Vector3.Distance(sound.EngineSource.transform.position,
                CityPortAssetProvider.FindPart(port.Vessel.gameObject,"ANCHOR_Engine").position),Is.LessThan(.001f));
            Assert.That(sound.EngineSource.isPlaying,Is.True);
            for(int crane=0;crane<2;crane++)
            {
                double time=CityPortCycle.UnloadStartSeconds+crane*CityPortCycle.CargoDurationSeconds+16d;
                port.ApplyAt(time,15f);
                for(int settle=0;settle<4;settle++)sound.Advance(time,.5f,true);
                AudioSource active=crane==0?sound.FirstCraneSource:sound.SecondCraneSource;
                AudioSource idle=crane==0?sound.SecondCraneSource:sound.FirstCraneSource;
                Assert.That(active.isPlaying,Is.True); Assert.That(idle.isPlaying,Is.False);
                Assert.That(Vector3.Distance(active.transform.position,
                    CityPortAssetProvider.FindPart(port.CraneBases[crane].gameObject,"ANCHOR_HoistFeed").position),Is.LessThan(.001f));
            }
            double trolleyTime=CityPortCycle.UnloadStartSeconds+34d;
            port.ApplyAt(trolleyTime,15f); sound.Advance(trolleyTime,.5f,true);
            Assert.That(sound.TrolleySource.isPlaying,Is.True);
            Assert.That(Vector3.Distance(sound.TrolleySource.transform.position,port.Trolley.position+Vector3.up*.2f),Is.LessThan(.001f));
            double landing=CityPortCycle.UnloadStartSeconds+CityPortCycle.LandedAtSeconds;
            port.ApplyAt(landing-.02d,15f); sound.Advance(landing-.02d,.02f,true);
            int contacts=sound.ContactsPlayed;
            port.ApplyAt(landing+.02d,15f); sound.Advance(landing+.02d,.04f,true);
            Assert.That(sound.ContactsPlayed,Is.EqualTo(contacts+1));
            Assert.That(Vector3.Distance(sound.ContactSource.transform.position,port.Cargo[0].position),Is.LessThan(.001f));
            sound.Advance(landing+.02d,.04f,true);
            Assert.That(sound.ContactsPlayed,Is.EqualTo(contacts+1),"A seek must not duplicate a landing.");

            cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.PortToFactory,.4f)); cannery.AdvanceSounds(true);
            prior=cannery.TruckEngineSource.transform.position;
            cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.PortToFactory,.6f)); cannery.AdvanceSounds(true);
            Assert.That(Vector3.Distance(prior,cannery.TruckEngineSource.transform.position),Is.GreaterThan(1f));
            Assert.That(Vector3.Distance(cannery.TruckEngineSource.transform.position,
                CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject,"ANCHOR_TruckEngine").position),Is.LessThan(.001f));
            Assert.That(cannery.TruckEngineSource.isPlaying,Is.True);
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f)); cannery.AdvanceSounds(true);
            Assert.That(cannery.SeamerSource.isPlaying,Is.True);
            Assert.That(cannery.TruckEngineSource.isPlaying||cannery.RetortSource.isPlaying,Is.False);
            Assert.That(Vector3.Distance(cannery.SeamerSource.transform.position,
                CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject,"MOVE_SeamerHead").position),Is.LessThan(.001f));
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Heat,.5f)); cannery.AdvanceSounds(true);
            Assert.That(cannery.RetortSource.isPlaying,Is.True);
            Assert.That(cannery.SeamerSource.isPlaying,Is.False);
            Vector3 retort=cannery.Plan.World(new Vector3(-5,1.35f,1.7f));
            Assert.That(Vector3.Distance(cannery.RetortSource.transform.position,retort),Is.LessThan(.001f));
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Cool,.5f)); cannery.AdvanceSounds(true);
            Assert.That(cannery.RetortSource.isPlaying,Is.False);
            Assert.That(Vector3.Distance(cannery.RetortSource.transform.position,retort),Is.LessThan(.001f),
                "Opening the retort door must not drag the circulation sound across the room.");

            cannery.ForcePresentation=port.ForcePresentation=false;
            city.Player.Motor.Teleport(cannery.Plan.Origin+new Vector3(2000,200,2000));
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f)); cannery.AdvanceSounds(true);
            sound.Advance(port.ElapsedSeconds,.5f,true);
            foreach(AudioSource source in new[]{cannery.TruckEngineSource,cannery.SeamerSource,cannery.RetortSource,cannery.ReverseAlarmSource,
                sound.EngineSource,sound.FirstCraneSource,sound.SecondCraneSource,sound.TrolleySource,sound.ContactSource})
                Assert.That(source.isPlaying,Is.False,source.name+" must release distant playback.");
            cannery.ForcePresentation=port.ForcePresentation=true;
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f)); cannery.AdvanceSounds(true);
            Assert.That(cannery.SeamerSource.isPlaying,Is.True,"Approach/seek restores the current work phase.");
        }

        private static IEnumerator CaptureProductionStereo(Camera camera,AudioSource source,Action tick,
            string name,string folder,Dictionary<AudioSource,bool> muted,List<string> report)
        {
            int rate=AudioSettings.outputSampleRate;
            double near=0;
            for(int shot=0;shot<3;shot++)
            {
                float distance=shot==2?source.maxDistance*.8f:3f;
                camera.transform.SetPositionAndRotation(source.transform.position+Vector3.right*distance,
                    Quaternion.LookRotation(shot==1?Vector3.back:Vector3.forward));
                tick(); source.Stop(); source.timeSamples=0; source.Play();
                var audible=new[]{source};
                yield return PumpProductionAudio(rate/2,null,muted,audible,tick);
                var pcm=new List<float>();
                yield return PumpProductionAudio(rate,pcm,muted,audible,tick);
                float[] samples=pcm.ToArray();
                double left=ProductionAudioRms(samples,0),right=ProductionAudioRms(samples,1);
                double rms=ProductionAudioRms(samples);
                string label=name+(shot==0?"-left":shot==1?"-turned-right":"-far");
                WritePcmWave(Path.Combine(folder,label+".wav"),samples,rate,2,1f);
                report.Add($"{label}: distance={distance:F2}m left={left:F6} right={right:F6} RMS={rms:F6}");
                TestContext.Out.WriteLine(report[report.Count-1]);
                if(shot==0)
                {
                    near=rms;
                    Assert.That(rms,Is.GreaterThan(.0002d),label+" must reach the actual mixer output.");
                    Assert.That(left,Is.GreaterThan(right*1.2d),"A mono source left of the listener must sound on the left.");
                }
                else if(shot==1)Assert.That(right,Is.GreaterThan(left*1.2d),
                    "Turning the real listener 180 degrees must reverse the stereo image.");
                else Assert.That(rms,Is.LessThan(near*.6d),"Real distance attenuation must reduce the captured output.");
            }
        }

        private static IEnumerator CaptureProductionReverb(Camera camera,AudioSource source,Action tick,
            string folder,Dictionary<AudioSource,bool> muted,List<string> report)
        {
            AudioClip original=source.clip;
            AudioReverbFilter reverb=source.GetComponent<AudioReverbFilter>();
            bool loop=source.loop,enabled=reverb.enabled;
            int rate=AudioSettings.outputSampleRate;
            AudioClip pulse=null;
            try
            {
                // Feed the real work PCM followed by exact silence through the
                // unchanged source gain, tone filter and mixer. The zero region
                // measures DSP reflections instead of a pre-recorded sound tail.
                int audibleFrames=Mathf.CeilToInt(original.frequency*.2f);
                var signal=new float[Mathf.CeilToInt(original.frequency*1.6f)];
                var dry=new float[audibleFrames];
                Assert.That(original.GetData(dry,0),Is.True);
                Array.Copy(dry,signal,dry.Length);
                pulse=AudioClip.Create("Production actual PCM and silence",signal.Length,1,original.frequency,false);
                Assert.That(pulse.SetData(signal,0),Is.True);
                source.clip=pulse; source.loop=true;
                camera.transform.SetPositionAndRotation(source.transform.position+Vector3.right*3f,Quaternion.LookRotation(Vector3.left));
                double dryTail=0;
                for(int pass=0;pass<2;pass++)
                {
                    source.Stop(); reverb.enabled=false;
                    yield return PumpProductionAudio(rate,null,muted,Array.Empty<AudioSource>(),null);
                    reverb.enabled=pass==1;
                    source.timeSamples=0; tick();
                    var pcm=new List<float>();
                    yield return PumpProductionAudio(Mathf.CeilToInt(rate*2f*1.2f),pcm,muted,new[]{source},tick);
                    float[] samples=pcm.ToArray();
                    double tail=ProductionAudioRms(samples,-1,Mathf.CeilToInt(rate*2f*.4f),Mathf.CeilToInt(rate*2f*.85f));
                    string label=pass==0?"factory-dry-reference":"factory-reverb-tail";
                    WritePcmWave(Path.Combine(folder,label+".wav"),samples,rate,2,1f);
                    report.Add($"{label}: tail RMS(0.40–0.85s)={tail:F8}");
                    TestContext.Out.WriteLine(report[report.Count-1]);
                    if(pass==0)dryTail=tail;
                    else
                    {
                        Assert.That(tail,Is.GreaterThan(.000005d),"Real zero-input reverb tail must reach the recording.");
                        Assert.That(tail,Is.GreaterThan(dryTail*1.15d),"The work-space filter must add a tail over the dry mixer reference.");
                    }
                }
            }
            finally
            {
                source.Stop(); source.clip=original; source.loop=loop; reverb.enabled=enabled;
                if(pulse!=null)Object.DestroyImmediate(pulse);
            }
        }

        private static IEnumerator CaptureProductionMusic(Camera camera,CityGameRoot city,CityCanneryController cannery,
            Action tick,string folder,Dictionary<AudioSource,bool> muted,List<string> report)
        {
            AudioSource music=city.Music.Source,seamer=cannery.SeamerSource;
            Assert.That(music.clip,Is.Not.Null);
            Assert.That(music.clip.loadState,Is.EqualTo(AudioDataLoadState.Loaded));
            Assert.That(music.volume,Is.EqualTo(CityMusicPlayer.ThemeOutputVolume).Within(.0001f));
            Assert.That(music.outputAudioMixerGroup,Is.SameAs(GameAudioMixer.MusicGroup));
            camera.transform.SetPositionAndRotation(cannery.Plan.World(new Vector3(-1.1f,1.9f,3.7f)),
                Quaternion.LookRotation(seamer.transform.position-cannery.Plan.World(new Vector3(-1.1f,1.9f,3.7f))));
            float distance=Vector3.Distance(camera.transform.position,seamer.transform.position);
            Assert.That(distance,Is.InRange(5f,6f),"The balance reference belongs to the public observation corridor.");
            int rate=AudioSettings.outputSampleRate;
            double machineRms=0,musicRms=0;
            for(int pass=0;pass<3;pass++)
            {
                seamer.Stop(); seamer.timeSamples=0; tick();
                music.Stop(); music.timeSamples=Mathf.Min(music.clip.samples-1,music.clip.frequency*20); music.Play();
                AudioSource[] audible=pass==0?new[]{seamer}:pass==1?new[]{music}:new[]{seamer,music};
                yield return PumpProductionAudio(rate/2,null,muted,audible,tick);
                var pcm=new List<float>();
                yield return PumpProductionAudio(rate*2,pcm,muted,audible,tick);
                float[] samples=pcm.ToArray();
                double rms=ProductionAudioRms(samples);
                string label=pass==0?"corridor-seamer":pass==1?"corridor-city-theme":"corridor-work-and-music";
                WritePcmWave(Path.Combine(folder,label+".wav"),samples,rate,2,1f);
                report.Add($"{label}: distance={distance:F2}m RMS={rms:F6} gain={20d*Math.Log10(Math.Max(rms,1e-12d)):F2}dBFS");
                TestContext.Out.WriteLine(report[report.Count-1]);
                Assert.That(rms,Is.GreaterThan(.0001d),label+" cannot be a silent comparison.");
                if(pass==0)machineRms=rms;
                if(pass==1)musicRms=rms;
            }
            report.Add($"corridor balance: seamer/music={20d*Math.Log10(machineRms/musicRms):F2}dB; " +
                $"Music bus={GameAudioMixer.MusicGainDb:F2}dB, source gain={music.volume:F3}");
            Assert.That(machineRms,Is.GreaterThanOrEqualTo(musicRms*.707d),
                "At the public corridor the real machine output must remain within 3 dB of the music or stronger.");
        }

        private static IEnumerator PumpProductionAudio(int target,List<float> sink,Dictionary<AudioSource,bool> muted,
            AudioSource[] audible,Action tick,int frameBudget=240,bool requireSamples=true)
        {
            int rendered=0;
            for(int frame=0;frame<frameBudget&&rendered<target;frame++)
            {
                yield return null;
                foreach(AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
                {
                    if(!muted.ContainsKey(source))muted.Add(source,source.mute);
                    source.mute=Array.IndexOf(audible,source)<0;
                }
                tick?.Invoke();
                int count=AudioRenderer.GetSampleCountForCaptureFrame();
                using var buffer=new NativeArray<float>(Math.Max(0,count)*2,Allocator.Temp);
                bool success=AudioRenderer.Render(buffer);
                if(requireSamples&&buffer.Length>0)Assert.That(success,Is.True);
                rendered+=buffer.Length;
                if(sink!=null)for(int i=0;i<buffer.Length;i++)sink.Add(buffer[i]);
            }
            if(requireSamples)Assert.That(rendered,Is.GreaterThanOrEqualTo(target),
                "Offline production capture must return real samples within its bounded frame budget.");
        }

        private static double ProductionAudioRms(float[] pcm,int channel=-1,int start=0,int end=int.MaxValue)
        {
            double sum=0;int count=0;bool finite=true;float peak=0;
            for(int i=Math.Max(0,start);i<Math.Min(end,pcm.Length);i++)
            {
                finite&=!float.IsNaN(pcm[i])&&!float.IsInfinity(pcm[i]);
                peak=Mathf.Max(peak,Mathf.Abs(pcm[i]));
                if(channel>=0&&i%2!=channel)continue;
                sum+=pcm[i]*pcm[i];count++;
            }
            Assert.That(finite,Is.True);
            Assert.That(peak,Is.LessThan(1f),"The recorded work mix must not clip.");
            Assert.That(count,Is.GreaterThan(0));
            return Math.Sqrt(sum/count);
        }
    }
}
