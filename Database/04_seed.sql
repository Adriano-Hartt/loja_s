/* =====================================================================
   Felibow - Banco de dados da loja (SQL Server 2017+)
   PIM IV - Etapa 7 - Projeto do Banco de Dados
   Arquivo 04: DADOS INICIAIS (catalogo)

   - Idempotente: so insere o que ainda nao existe (chave: Nome / CNPJ / SKU).
   - Nao cria usuarios nem senhas. Clientes se cadastram pelo site ou pelo app;
     para criar um administrador use dbo.sp_PromoverAdministrador.
   - Os CNPJ abaixo sao ficticios (dados de demonstracao).
   - Arquivo em UTF-8: ao usar sqlcmd, informe  -f 65001.
   ===================================================================== */

SET NOCOUNT ON;
GO

USE FelibowLoja;
GO

INSERT INTO dbo.Categorias (Nome, Descricao)
SELECT v.Nome, v.Descricao
  FROM (VALUES
        (N'Creatina',        N'Creatinas para força e performance'),
        (N'Whey Protein',    N'Proteínas para recuperação muscular'),
        (N'Vitaminas',       N'Vitaminas e multivitamínicos'),
        (N'Ômega 3',         N'Ácidos graxos essenciais'),
        (N'Minerais',        N'Minerais para a rotina esportiva'),
        (N'Aminoácidos',     N'Aminoácidos e recuperação'),
        (N'Saúde digestiva', N'Equilíbrio intestinal'),
        (N'Pré-treino',      N'Energia e foco para o treino')
       ) AS v (Nome, Descricao)
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Categorias c WHERE c.Nome = v.Nome);
GO

INSERT INTO dbo.Fornecedores (RazaoSocial, CNPJ, Email, Telefone)
SELECT v.RazaoSocial, v.CNPJ, v.Email, v.Telefone
  FROM (VALUES
        (N'NutriSul Distribuidora Ltda',   N'11.111.111/0001-11', N'contato@nutrisul.example',  N'(11) 4000-0001'),
        (N'VitaPrime Suplementos S.A.',    N'22.222.222/0001-22', N'contato@vitaprime.example', N'(11) 4000-0002')
       ) AS v (RazaoSocial, CNPJ, Email, Telefone)
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Fornecedores f WHERE f.CNPJ = v.CNPJ);
GO

INSERT INTO dbo.Produtos
    (Nome, SKU, PrecoVenda, PrecoCusto, QuantidadeEstoque, EstoqueMinimo, EstoqueMaximo,
     CategoriaId, FornecedorId, Marca, EdicaoEspecial, Descricao, Imagem, Status)
SELECT  v.Nome, v.SKU, v.PrecoVenda, v.PrecoCusto, v.Estoque, 5, 50,
        c.Id, f.Id, N'Felibow', v.EdicaoEspecial, v.Descricao, v.Imagem, N'Ativo'
  FROM (VALUES
        (N'VitaPrime — Creatina Monohidratada 300 g', N'PD-001', 149.90, 82.00, 24, N'Creatina', NULL, N'Creatina monohidratada pura, em pó, sem sabor. Suplemento alimentar para quem treina e busca apoio para força e desempenho. Fácil de misturar em água ou suco.', N'~/img/produtos/creatina.png'),
        (N'WheyCore — Whey Protein Chocolate 900 g', N'DW-002', 169.90, 95.00, 18, N'Whey Protein', NULL, N'Proteína do soro do leite com sabor chocolate. Suplemento alimentar para complementar a ingestão de proteínas na rotina de treino. Prático para o pós-treino.', N'~/img/produtos/whey.png'),
        (N'MultiVida — Multivitamínico 60 cápsulas', N'CE-003', 99.90, 52.00, 20, N'Vitaminas', NULL, N'Combinação de vitaminas e minerais para complementar a alimentação. Suplemento alimentar para a rotina do dia a dia.', N'~/img/produtos/multivitaminico.png'),
        (N'OceanPure — Ômega 3 120 cápsulas', N'BS-004', 129.90, 70.00, 14, N'Ômega 3', NULL, N'Óleo de peixe com EPA e DHA em cápsulas. Suplemento alimentar para complementar a dieta com ácidos graxos ômega 3.', N'~/img/produtos/omega3.png'),
        (N'MagnaBalance — Magnésio 60 cápsulas', N'MB-005', 89.90, 46.00, 16, N'Minerais', NULL, N'Suplemento alimentar de magnésio para complementar a dieta. Indicado para quem busca equilíbrio nutricional na rotina.', N'~/img/produtos/magnesio.png'),
        (N'SolVita — Vitamina D3 + K2 60 cápsulas', N'SP-006', 79.90, 40.00, 18, N'Vitaminas', NULL, N'Associação das vitaminas D3 e K2 em cápsulas. Suplemento alimentar para complementar a alimentação.', N'~/img/produtos/d3k2.png'),
        (N'GlutaFlow — Glutamina 300 g', N'MR-007', 109.90, 58.00, 12, N'Aminoácidos', NULL, N'Glutamina em pó para quem treina e quer complementar a nutrição esportiva. Pode ser misturada em água ou shakes.', N'~/img/produtos/glutamina.png'),
        (N'DuoPower — Pré-treino Frutas Vermelhas', N'DP-009', 94.90, 50.00, 15, N'Pré-treino', NULL, N'Suplemento alimentar em pó com cafeína, beta-alanina e taurina, no sabor frutas vermelhas. Para quem busca um complemento para a rotina de treino.', N'~/img/produtos/duopower.png'),
        (N'TurboVolt — Pré-treino Blueberry', N'TV-010', 99.90, 54.00, 15, N'Pré-treino', NULL, N'Suplemento alimentar em pó com cafeína, creatina e beta-alanina, no sabor blueberry. Para quem busca apoio na rotina de treino.', N'~/img/produtos/turbovolt.png')
       ) AS v (Nome, SKU, PrecoVenda, PrecoCusto, Estoque, Categoria, EdicaoEspecial, Descricao, Imagem)
       INNER JOIN dbo.Categorias c ON c.Nome = v.Categoria
       CROSS JOIN (SELECT TOP (1) Id FROM dbo.Fornecedores ORDER BY Id) f
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Produtos p WHERE p.SKU = v.SKU);
GO
