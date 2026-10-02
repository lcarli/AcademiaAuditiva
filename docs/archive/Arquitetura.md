# Arquitetura do Projeto Academia Auditiva

Este documento apresenta uma sugestão de arquitetura escalável e modular para o projeto **Academia Auditiva**, considerando boas práticas de design, separação de responsabilidades, expansão futura (como controle de assinatura) e performance.

---

## 1. 📊 Modelo de Dados (Tabelas, Tipos e Relações)

### Entidades principais:

#### **User**
- Id (GUID)
- Email
- PasswordHash
- Role (Admin, Aluno, etc.)
- LanguagePreference ("pt-BR", "en-US", ...)
- SubscriptionStatus (Ativo, Expirado, etc.)
- Data de Cadastro

#### **Exercise**
- Id
- Name ("GuessNote", etc.)
- Type (FK para tabela ExerciseType)
- Category (FK para tabela ExerciseCategory)
- Description
- Difficulty (FK para tabela DifficultyLevel)
- FiltersJson

#### **Score**
- Id
- UserId (FK)
- ExerciseId (FK)
- CorrectCount
- ErrorCount
- TimeSpentSeconds
- BestScore
- Timestamp
- FiltersUsedJson
- DifficultyUsed
- SessionId

#### **Badge**
- BadgeKey (string, PK)
- Title
- Description
- Icon (opcional)

#### **BadgesEarned**
- Id
- UserId
- BadgeKey (FK)
- EarnedDate
- IsNew

#### **ExerciseType** (Tabela para enum)
- Id
- Name ("ChordRecognition")
- DisplayName (localizável)

#### **ExerciseCategory** (Tabela para enum)
- Id
- Name ("Harmony")
- DisplayName

#### **DifficultyLevel** (Tabela para enum)
- Id
- Name (Beginner, Intermediate, Advanced)
- DisplayName

#### **Subscription** *(futuro)*
- Id
- UserId (FK)
- Plano (Mensal, Anual)
- Status (Ativo, Cancelado, Expirado)
- Data de Início / Término
- Gateway (Stripe, PayPal...)

---

## 2. 🚀 Arquitetura de Software (Classes e Estrutura)

### Padrão: MVC + Services + DTOs + APIs REST + Localization

### Controllers
- **ExerciseController** – Lida com exercícios individuais
- **DashboardController** – Estatísticas e visualizações
- **BadgeController** – Conquistas e alertas
- **ApiController** – Dados JSON para frontend (versão mobile ou SPA futura)
- **SubscriptionController** *(futuro)* – Controle de pagamentos e planos

### Services
- `ScoreService` – Salvar tentativas, avaliar desempenho
- `BadgeService` – Regras de conquista
- `ProgressAnalyzerService` – Identifica pontos fortes/fracos
- `ExerciseGeneratorService` – Cria os desafios com base nos filtros
- `MelodyEngine` – Gera melodias com métrica e ritmo
- `AuthService` – Login, tokens, registro
- `SubscriptionService` – Gerencia planos e cobranças (Stripe, etc)

### Interfaces e Models
- DTOs para envio e recebimento de dados (p. ex. `UserProgressDto`, `ExerciseSessionDto`)
- Interfaces como `IBadgeRuleEvaluator` para regras dinâmicas de badges

### Utils e Helpers
- `TheoryUtils` – Manipula escalas, intervalos, notas, etc.
- `AudioEngine.js` – Controle de áudio no navegador
- `Localizer` – Controle de tradução com arquivos RESX

### APIs REST (futuro suporte a SPA/mobile)
- `GET /api/user/progress-summary`
- `POST /api/score`
- `GET /api/user/badges`
- `GET /api/exercises`
- `POST /api/subscribe`

---

## 3. 📂 Módulos e Separacão por Domínio

### 📈 Dashboard
- Visualização pedagógica
- Gráficos por categoria, tipo, tempo, progresso
- Lista de badges conquistados e progresso

### 📘 Exercícios
- Visual por tipo de exercício
- Suporte a filtros dinâmicos
- Localização por idioma
- Animação, feedback, estatísticas

### 🏆 Conquistas (Badges)
- Atribuição dinâmica
- Feedback visual com Swal
- Armazenamento de progresso

### 🌐 Internacionalização
- Views localizadas com `@Localizer`
- Mensagens dinâmicas JS integradas com idioma do usuário

### 🏦 Assinaturas *(futuro)*
- Planos mensais e anuais
- Controle de acesso via middleware
- Integração com Stripe ou PayPal
- UI para gerenciamento de conta e pagamento

### 🌐 Admin Panel *(futuro)*
- CRUD de exercícios e filtros
- Visualização de progresso geral
- Gatilhos manuais de badges

---

## ✅ Considerações Finais

- O uso de enums deve ser limitado a locais não dinâmicos. Para maior controle e localização, usar tabelas `Lookup` no banco (como `ExerciseType`, `DifficultyLevel`).
- A arquitetura proposta separa bem responsabilidades e permite expansão.
- O sistema está pronto para integração com dashboard interativo, controle de assinatura e aplicativos mobile no futuro.

---