using System;
using Windows.Media.Playback;

namespace Clip
{
    public class VideoControls
    {
        private readonly MediaPlayer _mediaPlayer;
        private readonly TimeSpan _videoTimeOffset;
        public VideoControls(MediaPlayer mediaPlayer, TimeSpan videoTimeOffset)
        {
            _mediaPlayer = mediaPlayer;
            _videoTimeOffset = videoTimeOffset;
        }
        public void Backwards()
        {
            var currentPosition = _mediaPlayer.Position;
            var newPosition = currentPosition - _videoTimeOffset;
            if (newPosition < TimeSpan.Zero)
            {
                newPosition = TimeSpan.Zero;
            }

            _mediaPlayer.Position = newPosition;
        }
        public void Forwards()
        {
            var currentPosition = _mediaPlayer.Position;
            var newPosition = currentPosition + _videoTimeOffset;
            if (newPosition > _mediaPlayer.PlaybackSession.NaturalDuration)
            {
                newPosition = _mediaPlayer.PlaybackSession.NaturalDuration;
            }

            _mediaPlayer.Position = newPosition;
        }
        public void TogglePlayPause()
        {
            if (_mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            {
                _mediaPlayer.Pause();
            }
            else
            {
                _mediaPlayer.Play();
            }
        }
    }
}
