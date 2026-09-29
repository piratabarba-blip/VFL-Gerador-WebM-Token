# VFL Gerador WebM Token

Aplicativo Windows para transformar vídeos com fundo sólido — verde, azul ou outra cor — em tokens **WebM VP9 com transparência**, prontos para uso no Foundry VTT.

![Interface do VFL Gerador WebM Token](docs/interface.png)

## Principais recursos

- Detecção automática da cor de fundo.
- Pré-visualização transparente em referência fixa 1:1.
- Miniatura PNG automática com o mesmo enquadramento e quadriculado da prévia.
- Remoção de fundo com máscara alfa reforçada.
- Zoom e posicionamento horizontal/vertical com atualização automática.
- Saída fixa em 1080×1080, 24 FPS, VP9 com alfa.
- Preset confirmado com o perfil **Alpha VP9** do Kdenlive.
- Áudio opcional em Vorbis.
- Nomes sequenciais para evitar cache do Foundry.
- Processamento totalmente local; o vídeo original nunca é alterado.
- Interface escura.

## Requisito importante

Instale o [Kdenlive](https://kdenlive.org/) antes de usar. O programa utiliza automaticamente o FFmpeg 8 incluído no Kdenlive, pois foi o motor confirmado como compatível com transparência VP9 no Foundry VTT.

## Como usar

1. Baixe o ZIP na seção **Releases** e extraia a pasta inteira.
2. Abra `VFL.GeradorWebMToken.exe`.
3. Selecione o vídeo com fundo sólido.
4. Confira a prévia e ajuste zoom, posições X/Y, tolerância ou suavização se necessário.
5. Clique em **Gerar WebM**.
6. Importe o novo arquivo `_TOKEN.webm` no Foundry.

Reprodutores comuns podem mostrar o fundo verde porque não interpretam alfa em VP9. Dentro do Foundry, o fundo fica transparente.

## Configuração padrão da versão 1.0

- Tolerância: `0,023`
- Suavização: `0,30`
- Qualidade: Equilibrada (`CRF 15`)
- Bitrate: `20M`
- GOP: `15`
- Formato: `yuva420p`
- Áudio: Vorbis qualidade 4

## Compilar o código

Requisitos: Windows, .NET 8 SDK e Kdenlive instalado.

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true
```

## Licença

Uso pessoal e não comercial permitido. Venda e uso comercial são proibidos sem autorização. Este é um projeto **source-available**, não uma licença open source aprovada pela OSI.

Consulte [LICENSE.md](LICENSE.md) para os termos completos.
