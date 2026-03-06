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

class Program
{
	static int PAGE_SIZE = 4096;

	static int RAM_SIZE = 256 * 1024 * 1024;
	static int FRAME_COUNT = RAM_SIZE / PAGE_SIZE;

	static int BASE_REGISTER = 10000;
	static int LIMIT_REGISTER = 60000000;

	static int MEMORY_ACCESS_TIME = 100;
	static int PAGE_FAULT_TIME = 8000000;

	static List<Frame> frames = new List<Frame>();
	static Dictionary<int, int> frequency = new Dictionary<int, int>();
	static Dictionary<int, int> victimCount = new Dictionary<int, int>();
	static Dictionary<int, PageInfo> pageTable = new Dictionary<int, PageInfo>();

	static int hits = 0;
	static int faults = 0;
	static int replacements = 0;
	static int spatialLocality = 0;
	static int temporalLocality = 0;

	static int lastPage = -1;

	static Stopwatch reloj = new Stopwatch();

	static Random rand = new Random();

	static void Main()
	{
		Console.WriteLine("SIMULADOR AVANZADO DE MEMORIA VIRTUAL");
		Console.WriteLine("Algoritmo MFU\n");

		Console.WriteLine("BASE REGISTER: " + BASE_REGISTER);
		Console.WriteLine("LIMIT REGISTER: " + LIMIT_REGISTER);
		Console.WriteLine();

		Console.WriteLine("RAM SIMULADA: " + (RAM_SIZE / (1024 * 1024)) + " MB");
		Console.WriteLine("Tamaño pagina: " + PAGE_SIZE + " bytes");
		Console.WriteLine("Numero de frames: " + FRAME_COUNT);
		Console.WriteLine();

		for (int i = 0; i < FRAME_COUNT; i++)
			frames.Add(new Frame());

		string archivo = "dataset_memoria.bin";

		if (!File.Exists(archivo))
		{
			CrearArchivoGrande(archivo);
		}

		byte[] data = File.ReadAllBytes(archivo);

		Console.WriteLine("Tamaño del proceso: " + data.Length + " bytes");
		Console.WriteLine("Paginas virtuales: " + (data.Length / PAGE_SIZE));
		Console.WriteLine();

		List<int> accesos = GenerarPatronAcceso(data.Length, 50000);

		Console.WriteLine("Tiempo | DirLogica | Pag | Offset | DirFisica | Resultado | Frame | P | R | D | Victima | Memoria");

		reloj.Start();

		int tiempo = 0;

		foreach (int direccion in accesos)
		{
			tiempo++;

			if (direccion > LIMIT_REGISTER)
			{
				Console.WriteLine("VIOLACION DE MEMORIA");
				continue;
			}

			int pagina = direccion / PAGE_SIZE;
			int offset = direccion % PAGE_SIZE;

			if (!frequency.ContainsKey(pagina))
				frequency[pagina] = 0;

			frequency[pagina]++;

			if (!pageTable.ContainsKey(pagina))
				pageTable[pagina] = new PageInfo();

			DetectarLocalidad(pagina);

			bool hit = false;
			int frameIndex = -1;

			for (int i = 0; i < frames.Count; i++)
			{
				if (frames[i].Page == pagina)
				{
					hit = true;
					frameIndex = i;
					break;
				}
			}

			string victima = "-";

			if (hit)
			{
				hits++;
				pageTable[pagina].Reference = true;
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

					pageTable[mfu].Present = false;

					frames[frameIndex].Page = pagina;

					replacements++;
				}
			}

			pageTable[pagina].Present = true;
			pageTable[pagina].Reference = true;
			pageTable[pagina].Frame = frameIndex;

			if (rand.NextDouble() < 0.3)
				pageTable[pagina].Dirty = true;

			int direccionFisica = frameIndex * PAGE_SIZE + offset;

			Console.Write($"{tiempo,5} | {direccion,9} | {pagina,3} | {offset,6} | {direccionFisica,9} | ");

			if (hit)
				Console.Write("HIT      ");
			else
				Console.Write("FAULT    ");

			Console.Write($" | {frameIndex,5} | ");

			Console.Write($"{(pageTable[pagina].Present ? 1 : 0)} | ");
			Console.Write($"{(pageTable[pagina].Reference ? 1 : 0)} | ");
			Console.Write($"{(pageTable[pagina].Dirty ? 1 : 0)} | ");

			Console.Write($"{victima,6} | ");

			for (int i = 0; i < Math.Min(10, frames.Count); i++)
				Console.Write($"[{frames[i].Page}]");

			Console.Write("...");

			Console.WriteLine();

			RealizarOperacion(data, direccion);
		}

		reloj.Stop();

		MostrarMetricas(accesos.Count, data.Length);
	}

	static void CrearArchivoGrande(string path)
	{
		using (FileStream fs = new FileStream(path, FileMode.Create))
		{
			byte[] buffer = new byte[1024 * 1024 * 50];
			rand.NextBytes(buffer);
			fs.Write(buffer, 0, buffer.Length);
		}
	}

	static List<int> GenerarPatronAcceso(int limite, int cantidad)
	{
		List<int> lista = new List<int>();

		int baseAddr = rand.Next(0, limite);

		for (int i = 0; i < cantidad; i++)
		{
			if (rand.NextDouble() < 0.7)
				lista.Add(Math.Abs(baseAddr + rand.Next(-5000, 5000)) % limite);
			else
				lista.Add(rand.Next(0, limite));
		}

		return lista;
	}

	static void DetectarLocalidad(int pagina)
	{
		if (pagina == lastPage)
			temporalLocality++;

		if (Math.Abs(pagina - lastPage) == 1)
			spatialLocality++;

		lastPage = pagina;
	}

	static void RealizarOperacion(byte[] data, int direccion)
	{
		int suma = 0;

		for (int i = direccion; i < direccion + 100 && i < data.Length; i++)
			suma += data[i];

		double dummy = Math.Sqrt(suma);
	}

	static void MostrarMetricas(int total, int processSize)
	{
		Console.WriteLine("\n========== METRICAS DEL SISTEMA ==========");

		Console.WriteLine("Total accesos memoria: " + total);
		Console.WriteLine("Page Hits: " + hits);
		Console.WriteLine("Page Faults: " + faults);
		Console.WriteLine("Reemplazos MFU: " + replacements);

		double hitRatio = (double)hits / total;
		double faultRatio = (double)faults / total;

		Console.WriteLine("Hit Ratio: " + hitRatio.ToString("F4"));
		Console.WriteLine("Fault Ratio: " + faultRatio.ToString("F4"));

		double eat =
		(hitRatio * MEMORY_ACCESS_TIME) +
		(faultRatio * PAGE_FAULT_TIME);

		Console.WriteLine("\nTiempo de acceso efectivo (EAT): " + eat + " ns");

		Console.WriteLine("\nLocalidad temporal detectada: " + temporalLocality);
		Console.WriteLine("Localidad espacial detectada: " + spatialLocality);

		Console.WriteLine("\nFrecuencia de paginas (MFU):");

		foreach (var p in frequency.OrderByDescending(x => x.Value).Take(20))
		{
			Console.WriteLine("Pagina " + p.Key + " -> " + p.Value + " accesos");
		}

		Console.WriteLine("\nPaginas mas reemplazadas:");

		foreach (var v in victimCount.OrderByDescending(x => x.Value).Take(20))
		{
			Console.WriteLine("Pagina " + v.Key + " reemplazada " + v.Value + " veces");
		}

		Console.WriteLine("\nTABLA DE PAGINAS (primeras 20):");

		foreach (var p in pageTable.Take(20))
		{
			Console.WriteLine($"Pagina {p.Key} -> Frame {p.Value.Frame} | P:{(p.Value.Present ? 1 : 0)} R:{(p.Value.Reference ? 1 : 0)} D:{(p.Value.Dirty ? 1 : 0)}");
		}

		Console.WriteLine("\nEstado final de memoria (primeros 20 frames):");

		for (int i = 0; i < Math.Min(20, frames.Count); i++)
		{
			Console.WriteLine("Frame " + i + " -> Pagina " + frames[i].Page);
		}

		Console.WriteLine("\nTiempo total de simulacion: " + reloj.ElapsedMilliseconds + " ms");

		double memoryUtilization =
		(double)frames.Count(f => f.Page != -1) / frames.Count;

		Console.WriteLine("Utilizacion de memoria: " + (memoryUtilization * 100).ToString("F2") + "%");
	}
}
