using iText.IO.Util;
using Microsoft.Internal.VisualStudio.Shell;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tunnels
{
  struct Block
  {
    public int Type;
  }

  struct Chunk
  {
    public const int Width = 16;
    public const int Height = 16;

    public Guid Id { get; set; }
    public Block[] Blocks { get; set; }

    public static Chunk Empty {
      get {
        return new Chunk
        {
          Id = Guid.NewGuid(),
          Blocks = new Block[Width * Height]
        };
      }
    }
  }

  struct ChunkIndex : IEquatable<ChunkIndex>
  {
    public int X, Y, Z;

    public ChunkIndex(int x, int y, int z)
    {
      X = x;
      Y = y;
      Z = z;
    }

    public bool Equals(ChunkIndex other)
    {
      return X == other.X && Y == other.Y && Z == other.Z;
    }

    public override bool Equals(object obj)
    {
      return obj is ChunkIndex ? Equals((ChunkIndex)obj) : false;
    }

    public override int GetHashCode()
    {
      int hash = HashCode.EMPTY_HASH_CODE;
      hash = HashCode.Combine(hash, X);
      hash = HashCode.Combine(hash, Y);
      hash = HashCode.Combine(hash, Z);
      return hash;
    }
  }

  interface IChunkGenerator
  {
    Chunk Generate(ChunkIndex index);
  }

  class EmptyChunkGenerator : IChunkGenerator
  {
    public Chunk Generate(ChunkIndex index)
    {
      return Chunk.Empty;
    }
  }

  class Map
  {
    private readonly IChunkGenerator _generator;
    private readonly Dictionary<ChunkIndex, Chunk> _chunks = new Dictionary<ChunkIndex, Chunk>();

    public Map(IChunkGenerator generator)
    {
      _generator = generator;
    }

    private static int ArrayIndex(int width, int x, int y)
    {
      return y * width + x;
    }

    public Block GetBlockAtPos(int x, int y, int z)
    {
      var (chunk, blockIndex) = GetChunkAtBlock(x, y, z);
      return chunk.Blocks[ArrayIndex(Chunk.Width, blockIndex.x, blockIndex.y)];
    }

    public (ChunkIndex, (int x, int y)) GetChunkIndexAtBlock(int x, int y, int z)
    {
      var chunkIndex = new ChunkIndex { X = x / Chunk.Width, Y = y / Chunk.Height, Z = z };
      (int, int) blockIndex = (x % Chunk.Width, y % Chunk.Height);
      return (chunkIndex, blockIndex);
    }

    public Chunk GetChunk(ChunkIndex chunkIndex)
    {
      if (!_chunks.TryGetValue(chunkIndex, out Chunk chunk))
      {
        chunk = _generator.Generate(chunkIndex);
        _chunks.Add(chunkIndex, chunk);
      }

      return chunk;
    }

    public (Chunk, (int x, int y)) GetChunkAtBlock(int x, int y, int z)
    {
      var (chunkIndex, blockIndex) = GetChunkIndexAtBlock(x, y, z);
      var chunk = GetChunk(chunkIndex);
      return (chunk, blockIndex);
    }
  }

  class ChunkRenderer
  {
    private VertexPosition[] _vertexBuffer;
    private BasicEffect _effect;

    public ChunkRenderer(GraphicsDevice device)
    {
      _effect = new BasicEffect(device);
    }

    public void Update(Chunk chunk)
    {
      var vb = new List<VertexPosition>();

      for (int iy = 0; iy < Chunk.Height; ++iy)
      {
        for (int ix = 0; ix < Chunk.Width; ++ix)
        {
          float x = (float)ix;
          float y = (float)iy;
          Block block = chunk.Blocks[iy * Chunk.Width + ix];

          if (block.Type != 0)
          {
            vb.AddRange(new VertexPosition[6]
            {
              new VertexPosition(new Vector3(x,   y+1, 0)),
              new VertexPosition(new Vector3(x,   y,   0)),
              new VertexPosition(new Vector3(x+1, y,   0)),

              new VertexPosition(new Vector3(x+1, y,   0)),
              new VertexPosition(new Vector3(x+1, y+1, 0)),
              new VertexPosition(new Vector3(x,   y+1, 0)),
            });
          }
        }
      }

      _vertexBuffer = vb.ToArray();
    }

    public void Draw(Matrix projection, Matrix view)
    {
      if (_vertexBuffer != null)
      {
        _effect.Projection = projection;
        _effect.View = view;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
          pass.Apply();
          _effect.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList,
            _vertexBuffer, 0, _vertexBuffer.Length / 3);
        }
      }
    }
  }

  class RandomChunkGenerator : IChunkGenerator
  {
    private readonly Random _rng;

    public RandomChunkGenerator(Random rng)
    {
      _rng = rng;
    }

    public Chunk Generate(ChunkIndex index)
    {
      var chunk = Chunk.Empty;
      for (int i = 0; i < Chunk.Width * Chunk.Height; ++i)
        chunk.Blocks[i] = new Block { Type = _rng.Next(0, 100) % 2 };
      return chunk;
    }
  }


  class MapRenderer
  {
    private GraphicsDevice _device;
    private Map _map;

    private Dictionary<ChunkIndex, ChunkRenderer> _chunkRenderers = new Dictionary<ChunkIndex, ChunkRenderer>();

    private HashSet<ChunkIndex> _dirtyChunks = new HashSet<ChunkIndex>();

    public MapRenderer(GraphicsDevice device, Map map)
    {
      _device = device;
      _map = map;
    }

    public void Draw(Matrix projection, Matrix view, Rectangle viewport, int level)
    {
      //int minChunkX = viewport.X / 16;
      //int minChunkY = viewport.Y / 16;
      //int maxChunkX = (viewport.X + viewport.Width) / 16;
      //int maxChunkY = (viewport.Y + viewport.Height) / 16;

      int minChunkX = 0;
      int minChunkY = 0;
      int maxChunkX = 16;
      int maxChunkY = 16;

      // Todo: check this all works for negative indices (it doesn't)
      for (int y = minChunkY; y <= maxChunkY; ++y)
      {
        for (int x = minChunkX; x <= maxChunkX; ++x)
        {
          float chunkOriginX = x * Chunk.Width;
          float chunkOriginY = y * Chunk.Height;
          view.Translation += new Vector3(chunkOriginX, chunkOriginY, 0.0f);

          var chunkIndex = new ChunkIndex(x, y, level);
          var renderer = GetChunkRenderer(chunkIndex);
          var chunk = _map.GetChunk(chunkIndex);
          if (_dirtyChunks.Contains(chunkIndex))
          {
            renderer.Update(chunk);
            _dirtyChunks.Remove(chunkIndex);
          }
          renderer.Draw(projection, view);
        }
      }
    }

    private ChunkRenderer GetChunkRenderer(ChunkIndex chunkIndex)
    {
      if (!_chunkRenderers.TryGetValue(chunkIndex, out ChunkRenderer renderer))
      {
        renderer = new ChunkRenderer(_device);
        _dirtyChunks.Add(chunkIndex);
      }

      return renderer;
    }
  }

  /// <summary>
  /// This is the main type for your game.
  /// </summary>
  public class Tunnels : Game
  {
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private Random _rng;
    private Map _map;
    private MapRenderer _mapRenderer;

    public Tunnels()
    {
      _graphics = new GraphicsDeviceManager(this);
      Content.RootDirectory = "Content";
    }

    /// <summary>
    /// Allows the game to perform any initialization it needs to before starting to run.
    /// This is where it can query for any required services and load any non-graphic
    /// related content.  Calling base.Initialize will enumerate through any components
    /// and initialize them as well.
    /// </summary>
    protected override void Initialize()
    {
      _rng = new Random();
      _map = new Map(new RandomChunkGenerator(_rng));
      _mapRenderer = new MapRenderer(GraphicsDevice, _map);

      base.Initialize();
    }

    /// <summary>
    /// LoadContent will be called once per game and is the place to load
    /// all of your content.
    /// </summary>
    protected override void LoadContent()
    {
      // Create a new SpriteBatch, which can be used to draw textures.
      _spriteBatch = new SpriteBatch(GraphicsDevice);

      // TODO: use this.Content to load your game content here
    }

    /// <summary>
    /// UnloadContent will be called once per game and is the place to unload
    /// game-specific content.
    /// </summary>
    protected override void UnloadContent()
    {
      // TODO: Unload any non ContentManager content here
    }

    /// <summary>
    /// Allows the game to run logic such as updating the world,
    /// checking for collisions, gathering input, and playing audio.
    /// </summary>
    /// <param name="gameTime">Provides a snapshot of timing values.</param>
    protected override void Update(GameTime gameTime)
    {
      if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
        Exit();

      // TODO: Add your update logic here

      base.Update(gameTime);
    }

    /// <summary>
    /// This is called when the game should draw itself.
    /// </summary>
    /// <param name="gameTime">Provides a snapshot of timing values.</param>
    protected override void Draw(GameTime gameTime)
    {
      GraphicsDevice.Clear(Color.CornflowerBlue);

      (int width, int height) = (GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
      float aspect = (float)height / (float)width;
      var projection = Matrix.CreateOrthographicOffCenter(0.0f, (float)64.0f, (float)64.0f * aspect, 0.0f, -10.0f, 10.0f);
      //projection = Matrix.CreateOrthographic(1000.0f, 1000.0f, -10.0f, 10.0f);
      //var projection = Matrix.CreateOrthographic(128.0f, 128.0f * aspect, -10.0f, 10.0f);
      var view = Matrix.CreateTranslation(0.0f, 0.0f, 0.0f);

      var originalState = GraphicsDevice.RasterizerState;
      GraphicsDevice.RasterizerState = new RasterizerState { FillMode = FillMode.WireFrame, CullMode = CullMode.None };

      _mapRenderer.Draw(projection, view, GraphicsDevice.Viewport.Bounds, 0);

      GraphicsDevice.RasterizerState = originalState;

      base.Draw(gameTime);
    }
  }
}
