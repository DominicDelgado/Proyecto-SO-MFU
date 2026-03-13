using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

class Frame
{
	public int Page = -1;
}

class PageInfo
{
	public bool Present = false;
	public bool Reference = false;
	public bool Dirty = false;
	public int Frame = -1;
}

class TLBEntry
{
	public int Page;
	public int Frame;
}

class Program
{
	static int PAGE_SIZE = 4096;

	static int RAM_SIZE = 64 * 1024 * 1024;
	static int FRAME_COUNT = RAM_SIZE / PAGE_SIZE;

	static int PROCESS_SIZE = 200 * 1024 * 1024;

	static int MEMORY_ACCESS_TIME = 100;
	static int PAGE_FAULT_TIME = 8000000;

	static int TLB_SIZE = 16;

	static List<Frame> frames = new List<Frame>();
	static Dictionary<int, PageInfo> pageTable = new Dictionary<int, PageInfo>();
	static Dictionary<int, int> frequency = new Dictionary<int, int>();
	static Dictionary<int, int> victimCount = new Dictionary<int, int>();

	static List<TLBEntry> tlb = new List<TLBEntry>();

	static int hits = 0;
	static int faults = 0;
	static int replacements = 0;
	static int tlbHits = 0;
	static int tlbMiss = 0;

	static int spatialLocality = 0;
	static int temporalLocality = 0;

	static int lastPage = -1;

	static Stopwatch reloj = new Stopwatch();

	static Random rand = new Random();

	static void Main()
	{
		Console.WriteLine("==================================================");
		Console.WriteLine("SIMULADOR DE MEMORIA VIRTUAL - ALGORITMO MFU");
		Console.WriteLine("==================================================\n");

		Console.WriteLine("RAM simulada: " + RAM_SIZE / (1024 * 1024) + " MB");
		Console.WriteLine("Frames disponibles: " + FRAME_COUNT);
		Console.WriteLine("Page Size: " + PAGE_SIZE + " bytes");
		Console.WriteLine("Proceso simulado: " + PROCESS_SIZE / (1024 * 1024) + " MB\n");

		for (int i = 0; i < FRAME_COUNT; i++)
			frames.Add(new Frame());

		byte[] proceso = new byte[PROCESS_SIZE];
		rand.NextBytes(proceso);

		List<int> accesos = GenerarAccesos(PROCESS_SIZE, 120000);

		ImprimirEncabezado();

		reloj.Start();

		int tiempo = 0;

		foreach (var direccion in accesos)
		{
			tiempo++;

			int pagina = direccion / PAGE_SIZE;
			int offset = direccion % PAGE_SIZE;

			if (!frequency.ContainsKey(pagina))
				frequency[pagina] = 0;

			frequency[pagina]++;

			if (!pageTable.ContainsKey(pagina))
				pageTable[pagina] = new PageInfo();

			DetectarLocalidad(pagina);

			bool tlbHit = false;
			int frameIndex = -1;

			var tlbEntry = tlb.FirstOrDefault(x => x.Page == pagina);

			if (tlbEntry != null)
			{
				tlbHits++;
				tlbHit = true;
				frameIndex = tlbEntry.Frame;
			}
			else
			{
				tlbMiss++;
			}

			bool hit = false;
			string victima = "-";

			if (!tlbHit)
			{
				for (int i = 0; i < frames.Count; i++)
				{
					if (frames[i].Page == pagina)
					{
						hit = true;
						frameIndex = i;
						break;
					}
				}

				if (hit)
				{
					hits++;
				}
				else
				{
					faults++;

					int empty = frames.FindIndex(f => f.Page == -1);

					if (empty != -1)
					{
						frames[empty].Page = pagina;
						frameIndex = empty;
					}
					else
					{
						int mfu = frames
						.Select(f => f.Page)
						.OrderByDescending(p => frequency[p])
						.First();

						frameIndex = frames.FindIndex(f => f.Page == mfu);

						victima = mfu.ToString();

						if (!victimCount.ContainsKey(mfu))
							victimCount[mfu] = 0;

						victimCount[mfu]++;

						frames[frameIndex].Page = pagina;

						replacements++;
					}
				}

				AgregarTLB(pagina, frameIndex);
			}

			pageTable[pagina].Present = true;
			pageTable[pagina].Reference = true;
			pageTable[pagina].Frame = frameIndex;

			if (rand.NextDouble() < 0.25)
				pageTable[pagina].Dirty = true;

			int direccionFisica = frameIndex * PAGE_SIZE + offset;

			ImprimirFila(tiempo, direccion, pagina, offset, direccionFisica, hit, tlbHit, frameIndex, victima, pageTable[pagina]);

			if (tiempo % 500 == 0)
				LimpiarBitReferencia();

			if (tiempo % 1000 == 0)
				CambiarWorkingSet(PROCESS_SIZE);

			OperacionReal(proceso, direccion);
		}

		reloj.Stop();

		MostrarMetricas(accesos.Count);

		ImprimirLeyendaFinal();
	}

	static void ImprimirEncabezado()
	{
		Console.WriteLine("-------------------------------------------------------------------------------------------------------------------");
		Console.WriteLine(" T |  DIR_VIRTUAL | PAG | OFFSET | DIR_FISICA | RESULTADO | TLB | FR | P | R | D | VICTIMA ");
		Console.WriteLine("-------------------------------------------------------------------------------------------------------------------");
	}

	static void ImprimirFila(int t, int dv, int p, int off, int df, bool hit, bool tlb, int fr, string vic, PageInfo info)
	{
		Console.Write($"{t,4} | ");
		Console.Write($"{dv,12} | ");
		Console.Write($"{p,3} | ");
		Console.Write($"{off,6} | ");
		Console.Write($"{df,10} | ");

		if (hit)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(" HIT ");
		}
		else
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Write("FAULT");
		}

		Console.ResetColor();

		Console.Write($" | {(tlb ? "T" : "-"),3} | ");
		Console.Write($"{fr,3} | ");
		Console.Write($"{(info.Present ? 1 : 0)} | ");
		Console.Write($"{(info.Reference ? 1 : 0)} | ");
		Console.Write($"{(info.Dirty ? 1 : 0)} | ");
		Console.Write($"{vic,6}");

		Console.WriteLine();
	}

	static void ImprimirLeyendaFinal()
	{
		Console.WriteLine("\n================ EXPLICACION DE COLUMNAS =================");

		Console.WriteLine("T              : tiempo o numero de acceso a memoria");
		Console.WriteLine("DIR_VIRTUAL    : direccion virtual generada por la CPU");
		Console.WriteLine("PAG            : numero de pagina virtual");
		Console.WriteLine("OFFSET         : desplazamiento dentro de la pagina");
		Console.WriteLine("DIR_FISICA     : direccion fisica calculada por la MMU");
		Console.WriteLine("RESULTADO      : HIT si la pagina estaba en RAM / FAULT si hubo Page Fault");
		Console.WriteLine("TLB            : indica si hubo TLB Hit");
		Console.WriteLine("FR             : frame donde esta cargada la pagina");
		Console.WriteLine("P              : bit de presencia");
		Console.WriteLine("R              : bit de referencia");
		Console.WriteLine("D              : bit dirty (pagina modificada)");
		Console.WriteLine("VICTIMA        : pagina reemplazada por MFU");

		Console.WriteLine("\n'-' significa que no hubo evento en esa columna.");
	}

	static List<int> GenerarAccesos(int limite, int cantidad)
	{
		List<int> lista = new List<int>();

		int workingBase = rand.Next(0, limite / 2);

		for (int i = 0; i < cantidad; i++)
		{
			if (rand.NextDouble() < 0.6)
				lista.Add(Math.Abs(workingBase + rand.Next(-8000, 8000)) % limite);
			else
				lista.Add(rand.Next(0, limite));
		}

		return lista;
	}

	static void CambiarWorkingSet(int limite)
	{
		int nuevaBase = rand.Next(0, limite);
	}

	static void DetectarLocalidad(int pagina)
	{
		if (pagina == lastPage)
			temporalLocality++;

		if (Math.Abs(pagina - lastPage) == 1)
			spatialLocality++;

		lastPage = pagina;
	}

	static void LimpiarBitReferencia()
	{
		foreach (var p in pageTable)
			p.Value.Reference = false;
	}

	static void AgregarTLB(int page, int frame)
	{
		if (tlb.Count >= TLB_SIZE)
			tlb.RemoveAt(0);

		tlb.Add(new TLBEntry { Page = page, Frame = frame });
	}

	static void OperacionReal(byte[] data, int dir)
	{
		int suma = 0;

		for (int i = dir; i < dir + 200 && i < data.Length; i++)
			suma += data[i];

		double dummy = Math.Sqrt(suma);
	}

	static void MostrarMetricas(int total)
	{
		Console.WriteLine("\n================ METRICAS MMU =================");

		Console.WriteLine("Accesos totales: " + total);
		Console.WriteLine("Page Hits: " + hits);
		Console.WriteLine("Page Faults: " + faults);
		Console.WriteLine("Reemplazos MFU: " + replacements);

		Console.WriteLine("\nTLB Hits: " + tlbHits);
		Console.WriteLine("TLB Miss: " + tlbMiss);

		double hitRatio = (double)hits / total;
		double faultRatio = (double)faults / total;

		Console.WriteLine("\nHit Ratio: " + hitRatio.ToString("F4"));
		Console.WriteLine("Fault Ratio: " + faultRatio.ToString("F4"));

		double eat =
		(hitRatio * MEMORY_ACCESS_TIME) +
		(faultRatio * PAGE_FAULT_TIME);

		Console.WriteLine("\nEAT (Effective Access Time): " + eat + " ns");

		Console.WriteLine("\nLocalidad temporal: " + temporalLocality);
		Console.WriteLine("Localidad espacial: " + spatialLocality);

		Console.WriteLine("\nPaginas mas usadas:");

		foreach (var p in frequency.OrderByDescending(x => x.Value).Take(10))
		{
			Console.WriteLine("Pagina " + p.Key + " -> " + p.Value);
		}

		Console.WriteLine("\nTiempo simulacion: " + reloj.ElapsedMilliseconds + " ms");
	}
}
