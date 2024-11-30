
namespace AIL{
	public class Lzss
	{
		private static readonly int N = 4096;  // 0x1000 - size of ring buffer
		private static readonly int F = 18;    // Upper limit for match length
		private static readonly int THRESHOLD = 2;  // Minimum match length
		private static readonly int NIL = N;
		private static readonly int INIT_POS = 0xfee;  // Changed initial position

		private static int[] leftSon;
		private static int[] rightSon;
		private static int[] dad;
		private static int matchPosition;
		private static int matchLength;
		private static byte[] textBuf;

		public static byte[] Decompress(byte[] data)
		{
			int[] buffer = new int[N + F - 1];
			int r = INIT_POS;  // Changed to match custom format
			int c, i, j;
			uint flags = 0;

			// Initialize buffer with spaces
			for (i = 0; i < r; i++)
				buffer[i] = 0x20;

			using (var ms = new MemoryStream(data))
			using (var output = new MemoryStream())
			{
				while (true)
				{
					if (((flags >>= 1) & 0x100) == 0)
					{
						if ((c = ms.ReadByte()) == -1) break;
						flags = (uint)(c | 0xff00);
					}

					if ((flags & 1) == 0)  // Changed condition - 0 means literal
					{
						if ((c = ms.ReadByte()) == -1) break;
						output.WriteByte((byte)c);
						buffer[r++] = c;
						r &= N - 1;
					}
					else  // 1 means match
					{
						if ((i = ms.ReadByte()) == -1) break;
						if ((j = ms.ReadByte()) == -1) break;

						i |= (j & 0xf0) << 4;
						j = (j & 0x0f) + THRESHOLD;

						for (int k = 0; k <= j; k++)
						{
							c = buffer[(i + k) & (N - 1)];
							output.WriteByte((byte)c);
							buffer[r++] = c;
							r &= N - 1;
						}
					}
				}
				return output.ToArray();
			}
		}

		private static void Reset()
		{
			leftSon = new int[N + 1];
			rightSon = new int[N + 257];
			dad = new int[N + 1];
			textBuf = new byte[N + F - 1];
			matchLength = 0;
			matchPosition = 0;
		}

		private static void InitTree()
		{
			for (int i = N + 1; i <= N + 256; i++)
				rightSon[i] = NIL;
			for (int i = 0; i < N; i++)
				dad[i] = NIL;
		}

		private static void InsertNode(int r)
		{
			int i = 0;
			int p = N + 1 + textBuf[r];
			int cmp = 1;
			rightSon[r] = leftSon[r] = NIL;
			matchLength = 0;

			while (true)
			{
				if (cmp >= 0)
				{
					if (rightSon[p] != NIL)
						p = rightSon[p];
					else
					{
						rightSon[p] = r;
						dad[r] = p;
						return;
					}
				}
				else
				{
					if (leftSon[p] != NIL)
						p = leftSon[p];
					else
					{
						leftSon[p] = r;
						dad[r] = p;
						return;
					}
				}

				for (i = 1; i < F; i++)
				{
					if ((cmp = textBuf[r + i] - textBuf[p + i]) != 0)
						break;
				}

				if (i > matchLength)
				{
					matchPosition = p;
					if ((matchLength = i) >= F)
						break;
				}
			}

			dad[r] = dad[p];
			leftSon[r] = leftSon[p];
			rightSon[r] = rightSon[p];
			dad[leftSon[p]] = r;
			dad[rightSon[p]] = r;
			if (rightSon[dad[p]] == p)
				rightSon[dad[p]] = r;
			else
				leftSon[dad[p]] = r;
			dad[p] = NIL;
		}

		private static void DeleteNode(int p)
		{
			int q;
			if (dad[p] == NIL) return;

			if (rightSon[p] == NIL)
				q = leftSon[p];
			else if (leftSon[p] == NIL)
				q = rightSon[p];
			else
			{
				q = leftSon[p];
				if (rightSon[q] != NIL)
				{
					do
					{
						q = rightSon[q];
					} while (rightSon[q] != NIL);
					rightSon[dad[q]] = leftSon[q];
					dad[leftSon[q]] = dad[q];
					leftSon[q] = leftSon[p];
					dad[leftSon[p]] = q;
				}
				rightSon[q] = rightSon[p];
				dad[rightSon[p]] = q;
			}
			dad[q] = dad[p];
			if (rightSon[dad[p]] == p)
				rightSon[dad[p]] = q;
			else
				leftSon[dad[p]] = q;
			dad[p] = NIL;
		}

		public static byte[] Compress(byte[] data)
		{
			Reset();
			int r = INIT_POS;  // Changed to match custom format
			int s = 0;
			int len = 0;
			int i;
			int c, lastMatchLength, codeBufPtr;
			byte[] codeBuf = new byte[17];
			byte mask;

			InitTree();
			codeBuf[0] = 0;
			codeBufPtr = mask = 1;

			// Initialize buffer with spaces
			for (i = s; i < r; i++)
				textBuf[i] = 0x20;

			using (var input = new MemoryStream(data))
			using (var output = new MemoryStream())
			{
				for (len = 0; len < F && (c = input.ReadByte()) != -1; len++)
					textBuf[r + len] = (byte)c;
				
				if (len == 0)
					return new byte[0];

				for (i = 1; i <= F; i++)
					InsertNode(r - i);
				InsertNode(r);

				do
				{
					if (matchLength > len)
						matchLength = len;

					if (matchLength <= THRESHOLD)
					{
						matchLength = 1;
						// Don't set mask bit for literal (reversed from original)
						codeBuf[codeBufPtr++] = textBuf[r];
					}
					else
					{
						codeBuf[0] |= mask;  // Set mask bit for match (reversed from original)
						codeBuf[codeBufPtr++] = (byte)matchPosition;
						codeBuf[codeBufPtr++] = (byte)(((matchPosition >> 4) & 0xf0) | (matchLength - (THRESHOLD + 1)));
					}

					if ((mask <<= 1) == 0)
					{
						for (i = 0; i < codeBufPtr; i++)
							output.WriteByte(codeBuf[i]);
						codeBuf[0] = 0;
						codeBufPtr = mask = 1;
					}

					lastMatchLength = matchLength;
					for (i = 0; i < lastMatchLength && (c = input.ReadByte()) != -1; i++)
					{
						DeleteNode(s);
						textBuf[s] = (byte)c;
						if (s < F - 1)
							textBuf[s + N] = (byte)c;
						s = (s + 1) & (N - 1);
						r = (r + 1) & (N - 1);
						InsertNode(r);
					}

					while (i++ < lastMatchLength)
					{
						DeleteNode(s);
						s = (s + 1) & (N - 1);
						r = (r + 1) & (N - 1);
						if (--len != 0)
							InsertNode(r);
					}
				} while (len > 0);

				if (codeBufPtr > 1)
				{
					for (i = 0; i < codeBufPtr; i++)
						output.WriteByte(codeBuf[i]);
				}

				return output.ToArray();
			}
		}
	}
}
