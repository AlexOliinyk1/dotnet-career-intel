# Symfony Developer @ Smile Ukraine — Interview Battle Plan
## For Senior .NET Engineer Transitioning to Symfony

---

## PHASE 0: MINDSET (Read First)

**You are NOT a junior learning PHP. You are a Senior Backend Engineer who speaks a different dialect.**

Your 12+ years of .NET experience maps directly:
- ASP.NET Core DI Container = Symfony Service Container
- Entity Framework Core = Doctrine ORM
- MediatR = Symfony Messenger
- ASP.NET Middleware = Symfony Kernel Events
- NuGet = Composer
- `dotnet new` = Symfony CLI + MakerBundle

**Key phrase for the interview:**
> "Symfony's architecture is very close to how I already build systems: DI-first, service-oriented, ORM-based. For me this is a syntax change, not a paradigm shift."

---

## PHASE 1: 7-DAY SPEED PLAN

### Day 1: Environment + First Symfony App (4h)
```bash
# Install PHP 8.3+ and Composer
# Install Symfony CLI
curl -sS https://get.symfony.com/cli/installer | bash

# Create new project
symfony new smile-prep --webapp

# Start dev server
cd smile-prep
symfony server:start
```

**Tasks:**
- [ ] Install PHP 8.3, Composer, Symfony CLI
- [ ] Create a Symfony webapp project
- [ ] Understand directory structure (see mapping below)
- [ ] Create first controller with `php bin/console make:controller`
- [ ] Understand `config/services.yaml` (DI configuration)
- [ ] Run `php bin/console debug:container` — see all services

### Day 2: Doctrine ORM — Your EF Core (4h)
```bash
# Configure database in .env
# DATABASE_URL="mysql://root:password@127.0.0.1:3306/smile_prep"

php bin/console make:entity Product
php bin/console make:migration
php bin/console doctrine:migrations:migrate
```

**Tasks:**
- [ ] Create `Product`, `Category`, `Order` entities with relationships
- [ ] Run migrations
- [ ] Write custom repository methods (QueryBuilder)
- [ ] Test N+1 fix with fetch joins
- [ ] Understand `EntityManager->flush()` vs EF's `SaveChanges()`

### Day 3: REST API + Serialization (4h)
**Tasks:**
- [ ] Build CRUD API for Products (`/api/products`)
- [ ] Use DTOs (NOT expose entities directly)
- [ ] Symfony Serializer with groups
- [ ] Validation with `#[Assert\...]` attributes
- [ ] Proper HTTP status codes and error responses
- [ ] Test with Postman/curl

### Day 4: Messenger + Events (3h)
**Tasks:**
- [ ] Set up Symfony Messenger with async transport
- [ ] Create command/handler for async product import
- [ ] Use EventSubscriber for audit logging
- [ ] Understand Stamps, Envelopes, Middleware

### Day 5: Docker + CI/CD (3h)
**Tasks:**
- [ ] Dockerize the Symfony app (multi-stage build)
- [ ] docker-compose with PHP-FPM, Nginx, MySQL, Redis
- [ ] Basic CI pipeline (lint + tests)
- [ ] Review Azure DevOps pipeline syntax

### Day 6: React + Webpack Encore (2h)
**Tasks:**
- [ ] Install Webpack Encore (Symfony's Webpack wrapper)
- [ ] Add a simple React component
- [ ] Understand asset compilation flow

### Day 7: Mock Interview + Review (4h)
**Tasks:**
- [ ] Go through ALL questions in this document
- [ ] Answer each one OUT LOUD in English
- [ ] Time yourself (max 2 min per answer)
- [ ] Review weak spots

---

## PHASE 2: .NET → SYMFONY MAPPING (THE CHEAT SHEET)

### Project Structure
```
ASP.NET Core                    Symfony
─────────────                   ───────
Program.cs                  →   config/services.yaml + config/bundles.php
appsettings.json            →   .env / .env.local
Controllers/                →   src/Controller/
Services/                   →   src/Service/
Models/                     →   src/Entity/
Data/AppDbContext.cs        →   config/packages/doctrine.yaml
Migrations/                 →   migrations/
wwwroot/                    →   public/
*.csproj                    →   composer.json
NuGet                       →   Composer
launchSettings.json         →   .env (APP_ENV=dev)
```

### Dependency Injection

**ASP.NET Core:**
```csharp
// Program.cs
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Constructor injection
public class ProductController(IProductService productService)
{
    [HttpGet("/api/products")]
    public async Task<IActionResult> GetAll()
        => Ok(await productService.GetAllAsync());
}
```

**Symfony:**
```php
// config/services.yaml (autowiring is ON by default!)
services:
    _defaults:
        autowire: true
        autoconfigure: true

    App\:
        resource: '../src/'
        exclude:
            - '../src/Entity/'
            - '../src/Kernel.php'

// src/Controller/ProductController.php
#[Route('/api/products')]
class ProductController extends AbstractController
{
    public function __construct(
        private readonly ProductService $productService,
    ) {}

    #[Route('', methods: ['GET'])]
    public function getAll(): JsonResponse
    {
        return $this->json($this->productService->getAll());
    }
}
```

**Key differences:**
- Symfony autowires EVERYTHING by default (no manual registration needed)
- Service lifetimes: Symfony services are `shared` (singleton-like) by default
- Use `shared: false` in YAML for transient
- No built-in `Scoped` — use `#[AsDecorator]` or manual reset

### Controllers & Routing

**ASP.NET Core:**
```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> Get(int id) { ... }

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(
        [FromBody] CreateProductRequest request) { ... }
}
```

**Symfony:**
```php
#[Route('/api/products')]
class ProductController extends AbstractController
{
    #[Route('/{id}', methods: ['GET'])]
    public function get(int $id): JsonResponse { ... }

    #[Route('', methods: ['POST'])]
    public function create(Request $request): JsonResponse
    {
        $data = json_decode($request->getContent(), true);
        // or use #[MapRequestPayload] in Symfony 6.3+
        ...
    }
}
```

### Entity Framework Core → Doctrine ORM

**EF Core Entity:**
```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
    public Category Category { get; set; }
    public ICollection<Tag> Tags { get; set; }
}
```

**Doctrine Entity:**
```php
#[ORM\Entity(repositoryClass: ProductRepository::class)]
class Product
{
    #[ORM\Id]
    #[ORM\GeneratedValue]
    #[ORM\Column]
    private ?int $id = null;

    #[ORM\Column(length: 255)]
    private string $name;

    #[ORM\Column(type: 'decimal', precision: 10, scale: 2)]
    private string $price;

    #[ORM\ManyToOne(targetEntity: Category::class, inversedBy: 'products')]
    #[ORM\JoinColumn(nullable: false)]
    private Category $category;

    #[ORM\ManyToMany(targetEntity: Tag::class)]
    private Collection $tags;

    // Getters and setters...
}
```

**Key mapping:**
```
EF Core                         Doctrine
──────                          ────────
DbContext                   →   EntityManager
DbSet<T>                    →   Repository
.Include()                  →   JOIN FETCH (DQL) or QueryBuilder join
.AsNoTracking()             →   $em->detach() or read-only hint
SaveChanges()               →   $em->flush()
.Add()                      →   $em->persist()
.Remove()                   →   $em->remove()
LINQ                        →   DQL / QueryBuilder
Migrations (dotnet ef)      →   doctrine:migrations:migrate
```

**QueryBuilder (Doctrine) vs LINQ (EF Core):**

```php
// Doctrine QueryBuilder
$qb = $this->createQueryBuilder('p')
    ->join('p.category', 'c')
    ->where('c.name = :category')
    ->andWhere('p.price > :minPrice')
    ->setParameter('category', 'Electronics')
    ->setParameter('minPrice', 100)
    ->orderBy('p.price', 'DESC')
    ->setMaxResults(10)
    ->getQuery()
    ->getResult();
```

```csharp
// EF Core LINQ equivalent
var products = await context.Products
    .Include(p => p.Category)
    .Where(p => p.Category.Name == "Electronics" && p.Price > 100)
    .OrderByDescending(p => p.Price)
    .Take(10)
    .ToListAsync();
```

**N+1 Problem — Fix in Doctrine:**
```php
// BAD: N+1
$products = $repo->findAll(); // SELECT * FROM product
foreach ($products as $p) {
    $p->getCategory()->getName(); // SELECT each time!
}

// GOOD: Fetch join
$qb = $this->createQueryBuilder('p')
    ->join('p.category', 'c')
    ->addSelect('c') // ← This is the key! Like .Include()
    ->getQuery()
    ->getResult();
```

### Middleware → Kernel Events

**ASP.NET Core:**
```csharp
app.UseMiddleware<RequestLoggingMiddleware>();
```

**Symfony:**
```php
#[AsEventListener(event: KernelEvents::REQUEST, priority: 10)]
class RequestLoggingListener
{
    public function __invoke(RequestEvent $event): void
    {
        // Runs on every request, like middleware
    }
}
```

**Event order (like middleware pipeline):**
```
kernel.request      → Routing, auth, early returns
kernel.controller   → Before controller executes
kernel.response     → Modify response
kernel.terminate    → After response sent (logging, cleanup)
kernel.exception    → Error handling (like ExceptionHandler middleware)
```

### Symfony Messenger (≈ MassTransit / Background Services)

**Concept:** Command Bus + Event Bus + Async Processing

```php
// 1. Define message (like MediatR IRequest)
class ImportProductCommand
{
    public function __construct(
        public readonly string $sku,
        public readonly string $name,
        public readonly float $price,
    ) {}
}

// 2. Define handler (like MediatR IRequestHandler)
#[AsMessageHandler]
class ImportProductHandler
{
    public function __construct(
        private readonly EntityManagerInterface $em,
    ) {}

    public function __invoke(ImportProductCommand $command): void
    {
        $product = new Product($command->sku, $command->name, $command->price);
        $this->em->persist($product);
        $this->em->flush();
    }
}

// 3. Dispatch (like IMediator.Send())
class ProductController extends AbstractController
{
    #[Route('/api/products/import', methods: ['POST'])]
    public function import(MessageBusInterface $bus): JsonResponse
    {
        $bus->dispatch(new ImportProductCommand('SKU001', 'Widget', 29.99));
        return $this->json(['status' => 'queued']);
    }
}
```

**Async transport config (messenger.yaml):**
```yaml
framework:
    messenger:
        transports:
            async:
                dsn: '%env(MESSENGER_TRANSPORT_DSN)%' # amqp:// or doctrine://
                retry_strategy:
                    max_retries: 3
                    delay: 1000
                    multiplier: 2
        routing:
            App\Message\ImportProductCommand: async
```

### Security (≈ ASP.NET Core Identity + Auth)

```php
// JWT Authentication (lexik/jwt-authentication-bundle)
// security.yaml
security:
    firewalls:
        api:
            pattern: ^/api
            stateless: true
            jwt: ~
    access_control:
        - { path: ^/api/admin, roles: ROLE_ADMIN }
        - { path: ^/api, roles: ROLE_USER }
```

### Environment Config

```
ASP.NET Core                    Symfony
─────────                       ───────
appsettings.json            →   .env
appsettings.Development     →   .env.local
User Secrets                →   .env.local (git-ignored)
IConfiguration              →   %env(VAR_NAME)% in YAML
Environment.GetEnv          →   $_ENV['VAR_NAME']
```

---

## PHASE 3: INTERVIEW QUESTIONS & KILLER ANSWERS

### Block 1: "Why Symfony / Why the Switch?" (100% will be asked)

**Q: Why are you switching from .NET to PHP/Symfony?**
> "I'm not switching paradigms — I'm expanding my toolset. Symfony's architecture mirrors how I already think: DI container, service layer, ORM, event-driven processing. Over 12 years I've worked with multiple stacks, and the architectural principles are universal. For me, the transition is syntactic, not conceptual."

**Q: You're clearly overqualified for this level. Why this role?**
> "I'm looking for meaningful engineering work in a new ecosystem. My goal is to be a productive contributor from day one using my architectural experience, while building deep Symfony expertise. I find Smile's focus on open-source solutions and e-commerce particularly interesting, given my own e-commerce platform experience."

**Q: How quickly can you be productive in Symfony?**
> "I've already built a small API project in Symfony to validate my assumptions. The DI container, Doctrine ORM, and Messenger component feel very natural coming from ASP.NET Core. I'd estimate being productive within the first sprint, and fully autonomous within a month."

**Q: Won't you get bored? Won't you leave?**
> "I'm motivated by solving complex problems, not by a specific language. E-commerce at scale, B2B integrations, complex product data — these are the challenges I want, and Smile is the right place for them."

---

### Block 2: Symfony Core (Technical)

**Q: How does Symfony's DI Container work?**
> "Symfony compiles a service container at build time from configuration files. By default, autowiring matches type-hinted constructor parameters to registered services. Services are shared (singleton) by default. The container supports tagged services for patterns like Strategy, decorators with `#[AsDecorator]`, and factory methods. It's very similar to ASP.NET Core's built-in DI, but more feature-rich — closer to Autofac in capability."

**Q: Explain the Request lifecycle in Symfony.**
> "Request enters the Kernel, which dispatches `kernel.request` event (routing, auth), then `kernel.controller` (resolve controller), executes the controller, dispatches `kernel.response` (modify response), sends it, then `kernel.terminate` (cleanup). Exception at any point triggers `kernel.exception`. This is conceptually identical to ASP.NET Core's middleware pipeline."

**Q: What's the difference between EventListener and EventSubscriber?**
> "EventListener is configured externally (YAML or attribute) for a single event. EventSubscriber implements `EventSubscriberInterface` and declares which events it handles internally via `getSubscribedEvents()`. I prefer Listeners with PHP 8 attributes — cleaner, more explicit."

**Q: How do you handle configuration in Symfony?**
> ".env files for environment variables, with cascading: `.env` → `.env.local` → `.env.{APP_ENV}` → `.env.{APP_ENV}.local`. Secrets use `secrets:set` vault. Service parameters defined in `services.yaml`. This is equivalent to ASP.NET Core's `appsettings.json` + User Secrets + Environment Variables."

**Q: How does Symfony Messenger work?**
> "Messenger implements command/query bus pattern. You define Message classes (DTOs) and Handlers. Handlers are discovered via autowiring. Messages can be dispatched synchronously or routed to async transports (RabbitMQ, Doctrine, Redis). Supports retry strategies, dead-letter queues, and middleware pipeline. It's similar to MassTransit or MediatR + background services combined."

---

### Block 3: Doctrine ORM

**Q: How do you map entities in Doctrine?**
> "PHP 8 Attributes on entity classes. `#[ORM\Entity]`, `#[ORM\Column]`, relationship attributes like `#[ORM\ManyToOne]`, `#[ORM\OneToMany]`. Key concept: owning side vs inverse side — the owning side holds the foreign key and must be updated for persistence. This is different from EF Core where both sides are tracked."

**Q: How do you handle the N+1 problem?**
> "Three approaches: (1) Fetch joins in DQL/QueryBuilder with `addSelect()` — equivalent to EF's `.Include()`. (2) Set `fetch: EAGER` on the relationship attribute for always-needed relations. (3) Use `EXTRA_LAZY` for large collections where you only need count/contains. I prefer explicit fetch joins — same approach I use in EF Core."

**Q: Explain Doctrine's Unit of Work.**
> "EntityManager tracks all managed entities. `persist()` marks new entities for insertion. Changes to existing managed entities are detected automatically (dirty checking). `flush()` executes all pending SQL in a transaction — equivalent to EF Core's `SaveChanges()`. Important: if flush fails, the EntityManager becomes closed and must be reset."

**Q: How do you handle migrations?**
> "`make:migration` generates a diff between entities and current schema. `doctrine:migrations:migrate` applies them. In production, we use `--no-interaction` and always generate idempotent migrations. Same philosophy as EF Core: never drop columns in the same release as code changes."

**Q: QueryBuilder vs DQL — when to use which?**
> "QueryBuilder for dynamic queries where conditions are built programmatically (like EF Core's LINQ with conditional `.Where()`). DQL for static, complex queries — it's like writing LINQ as a string but entity-aware. For very complex reporting, I'd drop to native SQL with `DBAL`."

---

### Block 4: REST API

**Q: How do you structure a REST API in Symfony?**
> "Thin controllers that delegate to services. DTOs for request/response — never expose entities. Symfony Serializer with groups for different representations. Validation via `#[Assert\...]` attributes. Consistent error responses following RFC 7807 Problem Details. Pagination via query parameters with Link headers."

**Q: How do you handle authentication?**
> "JWT for stateless APIs using `lexik/jwt-authentication-bundle`. The security firewall validates tokens on each request. Refresh tokens for long sessions. Voters for fine-grained authorization. This maps directly to ASP.NET Core's JWT Bearer + Policy-based auth."

**Q: How do you version your APIs?**
> "URL-based versioning (`/api/v1/products`) for simplicity, or header-based (`Accept: application/vnd.app.v2+json`) for cleaner URLs. In Symfony, this can be handled via route prefixes, custom request listeners, or serialization groups per version."

---

### Block 5: DevOps & Infrastructure

**Q: How do you Dockerize a Symfony app?**
```dockerfile
# Multi-stage build
FROM php:8.3-fpm-alpine AS base
RUN docker-php-ext-install pdo_mysql opcache
COPY --from=composer:latest /usr/bin/composer /usr/bin/composer

FROM base AS build
WORKDIR /app
COPY composer.json composer.lock ./
RUN composer install --no-dev --optimize-autoloader
COPY . .
RUN php bin/console cache:clear --env=prod

FROM base AS production
COPY --from=build /app /app
```

**Q: Describe your CI/CD pipeline.**
> "Lint (PHP-CS-Fixer) → Static analysis (PHPStan level 8) → Unit tests (PHPUnit) → Integration tests → Build Docker image → Deploy to staging → Smoke tests → Deploy to production. In Azure DevOps, this is a multi-stage pipeline with approval gates."

**Q: Docker Compose for local development?**
```yaml
services:
  php:
    build: .
    volumes: ['.:/app']
    depends_on: [mysql, redis]
  nginx:
    image: nginx:alpine
    ports: ['8080:80']
  mysql:
    image: mysql:8.0
    environment:
      MYSQL_DATABASE: app
      MYSQL_ROOT_PASSWORD: secret
  redis:
    image: redis:alpine
  rabbitmq:
    image: rabbitmq:3-management
    ports: ['15672:15672']
```

---

### Block 6: Architecture & Patterns

**Q: How do you apply SOLID in Symfony?**
> "**S**: Thin controllers, business logic in services. **O**: Tagged services + interfaces for extensibility (like Strategy pattern). **L**: Doctrine repositories behind interfaces. **I**: Small, focused interfaces. **D**: Constructor injection everywhere, code to interfaces. Symfony's autowiring makes DI effortless."

**Q: How would you implement CQRS in Symfony?**
> "Two Messenger buses: command bus (write) and query bus (read). Commands are fire-and-forget or async. Queries return data synchronously. Separate read models for complex queries. This is the same pattern I used with MediatR in .NET, but Symfony Messenger has it built-in."

**Q: Describe a complex system you've built.**
> *Use your Strongestore e-commerce experience:*
> "I designed a multi-tenant e-commerce platform handling product catalogs with complex variant systems, real-time inventory, and payment integrations. Architecture: microservices with event-driven communication via RabbitMQ, CQRS for catalog reads, Docker/K8s deployment. The product data model is directly relevant to what Smile handles — complex hierarchies, multi-language content, and B2B pricing rules."

---

### Block 7: E-Commerce Specific (Smile Focus)

**Q: How do you handle complex product data?**
> "Three approaches depending on complexity: (1) EAV (Entity-Attribute-Value) for unlimited dynamic attributes — flexible but query-heavy. (2) JSON columns in MySQL 8+ for semi-structured data — good balance. (3) Product Family model (like Akeneo PIM) where each family defines its own attribute set. For search, sync to Elasticsearch with async indexing."

**Q: What do you know about Akeneo PIM?**
> "Akeneo is a PHP/Symfony-based Product Information Management system. It centralizes product data for multi-channel distribution. Integration via REST API with OAuth2 authentication. Supports product families, categories, attributes with validation rules, and channel-specific completeness. Smile is a key Akeneo partner, so I've studied its architecture."

**Q: B2B vs B2C e-commerce — key differences?**
> "B2B adds: company accounts with hierarchies, contract/negotiated pricing, approval workflows for orders, bulk ordering, credit limits, complex tax rules, and ERP integrations. In Symfony, I'd use the Workflow component for approval flows and Messenger for async ERP sync."

---

## PHASE 4: QUESTIONS YOU MUST ASK THEM

1. "What Symfony version are you on, and are there plans to upgrade?"
2. "How do you structure business logic — service layer, or do you use DDD?"
3. "What's your approach to testing? PHPUnit + Functional tests?"
4. "How big are the development teams per project?"
5. "What's the typical project — greenfield or maintaining existing systems?"
6. "Do you use API Platform or build APIs manually?"
7. "How do you handle technical debt?"

---

## PHASE 5: RED FLAGS TO AVOID

| DO NOT say | SAY instead |
|-----------|------------|
| "I want to learn PHP" | "I'm expanding my backend expertise" |
| "I used mostly .NET" | "My architectural experience is language-agnostic" |
| "I don't know Symfony well" | "Symfony's patterns are very familiar to me" |
| "I was a Tech Lead" | "I've led technical decisions and mentored teams" |
| "This is a step down" | "I'm focused on impactful engineering work" |

---

## PHASE 6: ENGLISH SURVIVAL PHRASES

**For explaining decisions:**
- "From my experience, the trade-off here is..."
- "In most projects I prefer... because..."
- "It depends on the use case. For high-throughput..."
- "The key consideration is..."

**For buying time:**
- "That's a great question. Let me think about this..."
- "There are several approaches. The most common ones are..."

**For demonstrating seniority:**
- "I've seen this pattern fail in production when..."
- "The important thing is not the implementation, but the trade-offs..."
- "I'd start with the simplest solution and iterate..."

---

## QUICK REFERENCE CARD (Print This)

```
Symfony CLI:
  symfony new project --webapp          # New project
  php bin/console make:controller       # Generate controller
  php bin/console make:entity           # Generate entity
  php bin/console make:migration        # Generate migration
  php bin/console doctrine:migrations:migrate  # Run migrations
  php bin/console debug:container       # List all services
  php bin/console debug:router          # List all routes
  php bin/console messenger:consume async  # Process async messages
  php bin/console cache:clear           # Clear cache
  composer require package-name         # Install package

Key Files:
  config/services.yaml    — DI configuration
  config/routes.yaml      — Routing (or use attributes)
  config/packages/*.yaml  — Bundle configuration
  .env                    — Environment variables
  src/Controller/         — Controllers
  src/Entity/             — Doctrine entities
  src/Repository/         — Doctrine repositories
  src/Service/            — Business logic
  src/Message/            — Messenger messages
  src/MessageHandler/     — Messenger handlers
  migrations/             — Database migrations

Doctrine EntityManager:
  $em->persist($entity)   — Track new entity
  $em->flush()            — Save all changes (like SaveChanges())
  $em->remove($entity)    — Mark for deletion
  $em->find(Product::class, $id)  — Find by ID
  $em->getRepository(Product::class)  — Get repository

Testing:
  php bin/phpunit                       # Run tests
  composer require --dev phpunit/phpunit
  composer require --dev symfony/test-pack
```

---

*Generated: February 2026 | Target: Smile Ukraine Symfony Developer Position*
*Based on: Job listing #318347 on DOU.ua*
