// MonoGame - Copyright (C) The MonoGame Team
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
﻿
using System;
using System.IO;

#if MONOMAC && PLATFORM_MACOS_LEGACY
using MonoMac.AudioToolbox;
using MonoMac.AudioUnit;
using MonoMac.AVFoundation;
using MonoMac.Foundation;
using MonoMac.OpenAL;
#elif OPENAL
using OpenTK.Audio.OpenAL;
#if IOS || MONOMAC
using AudioToolbox;
using AudioUnit;
using AVFoundation;
using Foundation;
#endif
#endif

namespace Microsoft.Xna.Framework.Audio
{
    public sealed partial class SoundEffect : IDisposable
    {
        internal const int MAX_PLAYING_INSTANCES = OpenALSoundController.MAX_NUMBER_OF_SOURCES;

        internal OALSoundBuffer SoundBuffer;

        internal float Rate { get; set; }

        internal int Size { get; set; }

        internal ALFormat Format { get; set; }

        #region Public Constructors

        private void PlatformLoadAudioStream(Stream s)
        {
            byte[] buffer;

#if OPENAL && !(MONOMAC || IOS)
            
            ALFormat format;
            int size;
            int freq;

            var stream = s;

            buffer = AudioLoader.Load(stream, out format, out size, out freq);

            Format = format;
            Size = size;
            Rate = freq;

#endif

#if MONOMAC || IOS

            var audiodata = new byte[s.Length];
            s.Read(audiodata, 0, (int)s.Length);

            using (AudioFileStream afs = new AudioFileStream (AudioFileType.WAVE))
            {
                afs.ParseBytes (audiodata, false);
                Size = (int)afs.DataByteCount;

                buffer = new byte[afs.DataByteCount];
                Array.Copy (audiodata, afs.DataOffset, buffer, 0, afs.DataByteCount);

                AudioStreamBasicDescription asbd = afs.DataFormat;
                int channelsPerFrame = asbd.ChannelsPerFrame;
                int bitsPerChannel = asbd.BitsPerChannel;

                // There is a random chance that properties asbd.ChannelsPerFrame and asbd.BitsPerChannel are invalid because of a bug in Xamarin.iOS
                // See: https://bugzilla.xamarin.com/show_bug.cgi?id=11074 (Failed to get buffer attributes error when playing sounds)
                if (channelsPerFrame <= 0 || bitsPerChannel <= 0)
                {
                    NSError err;
                    using (NSData nsData = NSData.FromArray(audiodata))
                    using (AVAudioPlayer player = AVAudioPlayer.FromData(nsData, out err))
                    {
                        channelsPerFrame = (int)player.NumberOfChannels;
                        bitsPerChannel = player.SoundSetting.LinearPcmBitDepth.GetValueOrDefault(16);

						Rate = (float)player.SoundSetting.SampleRate;
                        _duration = TimeSpan.FromSeconds(player.Duration);
                    }
                }
                else
                {
                    Rate = (float)asbd.SampleRate;
                    double duration = (Size / ((bitsPerChannel / 8) * channelsPerFrame)) / asbd.SampleRate;
                    _duration = TimeSpan.FromSeconds(duration);
                }

                if (channelsPerFrame == 1)
                    Format = (bitsPerChannel == 8) ? ALFormat.Mono8 : ALFormat.Mono16;
                else
                    Format = (bitsPerChannel == 8) ? ALFormat.Stereo8 : ALFormat.Stereo16;
            }

#endif
            // bind buffer
            SoundBuffer = new OALSoundBuffer();
            SoundBuffer.BindDataBuffer(buffer, Format, Size, (int)Rate);
        }

        private void PlatformInitializePCM(byte[] buffer, int offset, int count, int sampleRate, AudioChannels channels, int loopStart, int loopLength)
        {
            Rate = (float)sampleRate;
            Size = (int)count;
            Format = channels == AudioChannels.Stereo ? ALFormat.Stereo16 : ALFormat.Mono16;

            // bind buffer
            SoundBuffer = new OALSoundBuffer();
            SoundBuffer.BindDataBuffer(buffer, Format, Size, (int)Rate);
        }

        private void PlatformInitializeFormat(byte[] buffer, int format, int sampleRate, int channels, int blockAlignment, int loopStart, int loopLength)
        {
            // We need to decode MSADPCM.
            if (format == 2)
            {
                using (var stream = new MemoryStream(buffer))
                using (var reader = new BinaryReader(stream))
                {
                    buffer = MSADPCMToPCM.MSADPCM_TO_PCM(
                        reader,
                        (short)channels,
                        (short)((blockAlignment / channels) - 22));

                    format = 1;
                }
            }

            if (format != 1)
                throw new NotSupportedException("Unsupported wave format!");

            PlatformInitializePCM(buffer, 0, buffer.Length, sampleRate, (AudioChannels)channels, loopStart, loopLength);
        }
        
        #endregion

        #region Additional SoundEffect/SoundEffectInstance Creation Methods

        private void PlatformSetupInstance(SoundEffectInstance inst)
        {
            inst.InitializeSound();
        }

        #endregion

        #region IDisposable Members

        private void PlatformDispose(bool disposing)
        {
            if (SoundBuffer != null)
            {
                SoundBuffer.Dispose();
                SoundBuffer = null;
            }
        }

        #endregion

        internal static void PlatformShutdown()
        {
            OpenALSoundController.DestroyInstance();
        }
    }
}

