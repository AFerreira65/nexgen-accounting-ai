# Especificações dos webservices da AT

Cópias, tal como publicadas pela AT, dos WSDL/XSD e dos manuais de integração
dos serviços que a Fase 3 usa (Bloco C). Descarregadas em 2026-10-03 de
`info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico/Webservice/`
— a página «Testar Webservice» da área de produtores de software aponta para
os mesmos documentos, mas exige sessão iniciada.

| Pasta | Serviço | Ficheiros |
|---|---|---|
| `e-fatura/` | Consulta de faturas (adquirente ou emitente) | `fatshareInvoices.wsdl`, manual específico, manual de aspetos gerais |
| `iva-periodica/` | Declaração periódica de IVA | WSDL, manual (2025-07-31) |
| `iva-recapitulativa/` | Declaração recapitulativa do IVA | `driva.wsdl`, `driva-ws.xsd`, manual |
| `irc/` | Modelo 22 | WSDL, manual |
| `ies/` | IES / Declaração Anual | WSDL, manual |
| `obrigacoes-acessorias/` | DMR, Modelos 10, 25, 30, 39, … | `oaatws/` (WSDL e XSD), manual |
| `dmis/` | Declaração Mensal de Imposto do Selo | WSDL, XSD, manual |

Não estão aqui, de propósito: o WSDL de registo de faturas (`Fatcorews.wsdl`
— a aplicação não comunica faturas), as aplicações cliente de teste da AT, e
qualquer certificado ou chave.

São documentos da AT: não se editam. Quando a AT publicar uma versão nova
(o WSDL da declaração periódica muda no início de 2027), substitui-se o
ficheiro e regista-se a data aqui.
