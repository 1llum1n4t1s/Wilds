// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace Wilds.App.Data.Models
{
	public sealed partial class DisposableArray : FreeableStore<DisposableArray>
	{
		public byte[] Bytes { get; }

		public DisposableArray(byte[] array)
		{
			Bytes = array;
		}

		public override DisposableArray CreateCopy()
		{
			return new DisposableArray(Bytes.CloneArray());
		}

		public override bool Equals(DisposableArray? other)
		{
			if (other?.Bytes is null || Bytes is null)
				return false;

			return Bytes.SequenceEqual(other.Bytes);
		}

		public override int GetHashCode()
		{
			return Bytes.GetHashCode();
		}

		protected override void SecureFree()
		{
			EnsureSecureDisposal(Bytes);
		}

		/// <summary>
		/// バイト配列をゼロ埋めして破棄する。
		/// Why: Array.Clear のみだと GC 移動時に旧アドレスへコピーが残存する可能性がある。
		/// 先に Pinned で固定してから Unsafe writer でゼロ埋めし、移動を防ぐ。
		/// </summary>
		internal static unsafe void EnsureSecureDisposal(byte[] buffer)
		{
			if (buffer is null || buffer.Length == 0) return;

			var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
			try
			{
				byte* p = (byte*)handle.AddrOfPinnedObject();
				for (int i = 0; i < buffer.Length; i++)
					p[i] = 0;
			}
			finally
			{
				handle.Free();
			}
		}

		public static implicit operator byte[](DisposableArray disposableArray)
		{
			return disposableArray.Bytes;
		}
	}
}
