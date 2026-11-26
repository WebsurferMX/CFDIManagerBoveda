-- Esquema inicial de cfdi_manager para una base vacía (modo Escritorio / Local).
-- Generado con pg_dump --schema-only --no-owner --no-privileges desde la base del servidor (26/11/2026).
-- Lo aplica EsquemaInicial.cs solo cuando no existe la tabla cfdi; las tablas y columnas nuevas siguen
-- creándose con los Asegurar* al arrancar. Regenerarlo si cambian las tablas básicas (cfdi, empresas...).

--
-- PostgreSQL database dump
--



SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: fn_clientes_facturacion(date, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_clientes_facturacion(p_fecha_inicio date, p_fecha_fin date) RETURNS TABLE(rfc character varying, nombre character varying, cantidad_cfdi bigint, total_facturado numeric)
    LANGUAGE sql
    AS $$
SELECT
    p.rfc,
    p.nombre,
    COUNT(*) cantidad_cfdi,
    SUM(c.total) total_facturado
FROM cfdi c
INNER JOIN personas_fiscales p
    ON c.receptor_id = p.persona_id
WHERE c.fecha_emision BETWEEN
    p_fecha_inicio
    AND
    p_fecha_fin
GROUP BY
    p.rfc,
    p.nombre;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: archivos_cfdi; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.archivos_cfdi (
    archivo_id bigint NOT NULL,
    cfdi_id bigint NOT NULL,
    tipo_archivo character varying(20),
    nombre_archivo character varying(500),
    ruta_archivo text,
    fecha_registro timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


--
-- Name: archivos_cfdi_archivo_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.archivos_cfdi_archivo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: archivos_cfdi_archivo_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.archivos_cfdi_archivo_id_seq OWNED BY public.archivos_cfdi.archivo_id;


--
-- Name: cfdi; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cfdi (
    cfdi_id bigint NOT NULL,
    empresa_id bigint NOT NULL,
    uuid character varying(36) NOT NULL,
    tipo_cfdi character(1) NOT NULL,
    emisor_id bigint,
    receptor_id bigint,
    serie character varying(50),
    folio character varying(50),
    fecha_emision timestamp without time zone NOT NULL,
    fecha_timbrado timestamp without time zone,
    subtotal numeric(18,2),
    total numeric(18,2),
    moneda character varying(10),
    estado_sat character varying(30) DEFAULT 'PENDIENTE'::character varying,
    ruta_xml text,
    fecha_importacion timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    clasificacion character varying(20),
    monto_total_pagos numeric(18,2),
    forma_pago character varying(10),
    metodo_pago character varying(10),
    uso_cfdi character varying(10),
    lugar_expedicion character varying(10),
    regimen_fiscal_emisor character varying(10),
    domicilio_fiscal_receptor character varying(10),
    regimen_fiscal_receptor character varying(10),
    descuento numeric(18,6),
    total_impuestos_trasladados numeric(18,6),
    total_impuestos_retenidos numeric(18,6),
    rfc_prov_certif character varying(20),
    version_cfdi character varying(10),
    tipo_cambio numeric(18,6),
    condiciones_pago text,
    exportacion character varying(10),
    no_certificado character varying(40),
    confirmacion character varying(20),
    fac_atr_adquirente character varying(30),
    residencia_fiscal character varying(10),
    num_reg_id_trib character varying(50),
    no_certificado_sat character varying(40),
    version_timbre character varying(10),
    certificado text,
    sello_cfdi text,
    sello_cfd text,
    sello_sat text,
    es_cancelable character varying(80),
    estatus_cancelacion character varying(120),
    validacion_efos character varying(120),
    codigo_estatus_sat character varying(250),
    fecha_validacion_sat timestamp without time zone,
    origen_validacion_sat text,
    pago_iva_trasladado numeric(18,2),
    pago_iva_retenido numeric(18,2),
    pago_isr_retenido numeric(18,2),
    CONSTRAINT chk_cfdi_clasificacion CHECK (((clasificacion)::text = ANY (ARRAY[('EMITIDO'::character varying)::text, ('RECIBIDO'::character varying)::text, ('PROPIO'::character varying)::text, ('NO_IDENTIFICADO'::character varying)::text])))
);


--
-- Name: cfdi_cfdi_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cfdi_cfdi_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cfdi_cfdi_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cfdi_cfdi_id_seq OWNED BY public.cfdi.cfdi_id;


--
-- Name: cfdi_conceptos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cfdi_conceptos (
    concepto_id bigint NOT NULL,
    cfdi_id bigint NOT NULL,
    clave_prod_serv character varying(20),
    descripcion text,
    cantidad numeric(18,4),
    valor_unitario numeric(18,4),
    importe numeric(18,2),
    no_identificacion character varying(100),
    clave_unidad character varying(20),
    unidad character varying(100),
    descuento numeric(18,6),
    objeto_imp character varying(10),
    cuenta_predial character varying(100)
);


--
-- Name: cfdi_conceptos_concepto_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cfdi_conceptos_concepto_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cfdi_conceptos_concepto_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cfdi_conceptos_concepto_id_seq OWNED BY public.cfdi_conceptos.concepto_id;


--
-- Name: cfdi_importaciones; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cfdi_importaciones (
    importacion_id bigint NOT NULL,
    archivo character varying(500) NOT NULL,
    uuid character varying(50),
    fecha_importacion timestamp without time zone DEFAULT now() NOT NULL,
    estatus character varying(20) NOT NULL,
    mensaje_error text
);


--
-- Name: cfdi_importaciones_importacion_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cfdi_importaciones_importacion_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cfdi_importaciones_importacion_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cfdi_importaciones_importacion_id_seq OWNED BY public.cfdi_importaciones.importacion_id;


--
-- Name: cfdi_impuestos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cfdi_impuestos (
    impuesto_id bigint NOT NULL,
    concepto_id bigint NOT NULL,
    impuesto character varying(10),
    base numeric(18,2),
    tasa numeric(18,6),
    importe numeric(18,2),
    tipo character varying(20),
    tipo_factor character varying(20)
);


--
-- Name: cfdi_impuestos_globales; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cfdi_impuestos_globales (
    impuesto_global_id bigint NOT NULL,
    cfdi_id bigint NOT NULL,
    tipo character varying(20) NOT NULL,
    impuesto character varying(10),
    base numeric(18,6),
    tipo_factor character varying(20),
    tasa numeric(18,6),
    importe numeric(18,6)
);


--
-- Name: cfdi_impuestos_globales_impuesto_global_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.cfdi_impuestos_globales ALTER COLUMN impuesto_global_id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.cfdi_impuestos_globales_impuesto_global_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: cfdi_impuestos_impuesto_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cfdi_impuestos_impuesto_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cfdi_impuestos_impuesto_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cfdi_impuestos_impuesto_id_seq OWNED BY public.cfdi_impuestos.impuesto_id;


--
-- Name: cfdi_relacionados; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cfdi_relacionados (
    relacionado_id bigint NOT NULL,
    cfdi_id bigint NOT NULL,
    tipo_relacion character varying(10) NOT NULL,
    uuid_relacionado character varying(36) NOT NULL
);


--
-- Name: cfdi_relacionados_relacionado_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.cfdi_relacionados ALTER COLUMN relacionado_id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.cfdi_relacionados_relacionado_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: cfdi_verificados; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cfdi_verificados (
    cfdi_id bigint NOT NULL,
    fecha_marca timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: configuracion_app; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.configuracion_app (
    clave text NOT NULL,
    valor text NOT NULL,
    actualizado timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: configuracion_correo; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.configuracion_correo (
    configuracion_id integer DEFAULT 1 NOT NULL,
    servidor text DEFAULT ''::text NOT NULL,
    puerto integer DEFAULT 587 NOT NULL,
    seguridad text DEFAULT 'AUTO'::text NOT NULL,
    usuario text DEFAULT ''::text NOT NULL,
    password text,
    remitente_email text DEFAULT ''::text NOT NULL,
    remitente_nombre text DEFAULT 'CFDI Manager'::text NOT NULL,
    url_aplicacion text DEFAULT ''::text NOT NULL,
    habilitado boolean DEFAULT false NOT NULL,
    fecha_actualizacion timestamp without time zone DEFAULT now() NOT NULL,
    CONSTRAINT configuracion_correo_configuracion_id_check CHECK ((configuracion_id = 1))
);


--
-- Name: correos_enviados; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.correos_enviados (
    correo_id bigint NOT NULL,
    fecha timestamp without time zone DEFAULT now() NOT NULL,
    tipo text NOT NULL,
    destinatarios text NOT NULL,
    asunto text NOT NULL,
    adjuntos text DEFAULT ''::text NOT NULL,
    enviado boolean NOT NULL,
    error text DEFAULT ''::text NOT NULL,
    enviado_por text DEFAULT ''::text NOT NULL,
    empresa_id bigint
);


--
-- Name: correos_enviados_correo_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.correos_enviados_correo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: correos_enviados_correo_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.correos_enviados_correo_id_seq OWNED BY public.correos_enviados.correo_id;


--
-- Name: efos_sat; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.efos_sat (
    id integer NOT NULL,
    rfc character varying(13) NOT NULL,
    razon_social text,
    situacion character varying(30) NOT NULL,
    fecha_publicacion date,
    fecha_importacion timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    oficio_presuncion text,
    pub_presunto_sat date,
    pub_presunto_dof date,
    pub_desvirtuado_dof date,
    oficio_definitivo text,
    pub_definitivo_sat date,
    pub_definitivo_dof date,
    pub_favorable_dof date,
    procedimientos integer DEFAULT 1 NOT NULL
);


--
-- Name: efos_sat_cargas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.efos_sat_cargas (
    carga_id integer NOT NULL,
    fecha_corte date,
    fecha_carga timestamp without time zone DEFAULT now() NOT NULL,
    origen text NOT NULL,
    registros integer NOT NULL
);


--
-- Name: efos_sat_cargas_carga_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.efos_sat_cargas_carga_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: efos_sat_cargas_carga_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.efos_sat_cargas_carga_id_seq OWNED BY public.efos_sat_cargas.carga_id;


--
-- Name: efos_sat_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.efos_sat_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: efos_sat_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.efos_sat_id_seq OWNED BY public.efos_sat.id;


--
-- Name: empresa_certificados; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.empresa_certificados (
    certificado_id bigint NOT NULL,
    empresa_id bigint NOT NULL,
    rfc character varying(13) NOT NULL,
    tipo_certificado character varying(20) NOT NULL,
    nombre_certificado character varying(250),
    numero_serie character varying(100),
    fecha_inicio timestamp without time zone,
    fecha_fin timestamp without time zone,
    certificado_cer bytea NOT NULL,
    llave_key bytea NOT NULL,
    password_cifrado text NOT NULL,
    activo boolean DEFAULT true NOT NULL,
    fecha_carga timestamp without time zone DEFAULT now() NOT NULL,
    fecha_actualizacion timestamp without time zone,
    usuario_carga character varying(100)
);


--
-- Name: empresa_certificados_certificado_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.empresa_certificados_certificado_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: empresa_certificados_certificado_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.empresa_certificados_certificado_id_seq OWNED BY public.empresa_certificados.certificado_id;


--
-- Name: empresa_certificados_log; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.empresa_certificados_log (
    log_id bigint NOT NULL,
    certificado_id bigint NOT NULL,
    fecha_evento timestamp without time zone DEFAULT now() NOT NULL,
    usuario character varying(100),
    accion character varying(50),
    observaciones text
);


--
-- Name: empresa_certificados_log_log_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.empresa_certificados_log_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: empresa_certificados_log_log_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.empresa_certificados_log_log_id_seq OWNED BY public.empresa_certificados_log.log_id;


--
-- Name: empresas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.empresas (
    empresa_id bigint NOT NULL,
    rfc character varying(13) NOT NULL,
    razon_social character varying(300) NOT NULL,
    regimen_fiscal character varying(10),
    fecha_alta timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    nombre_corto character varying(100),
    ruta_xml character varying(1000),
    activa boolean DEFAULT true NOT NULL,
    fecha_registro timestamp without time zone DEFAULT now() NOT NULL,
    sat_usuario character varying(100),
    sat_password_cifrada text,
    ruta_cer character varying(500),
    ruta_key character varying(500),
    password_key_cifrada text,
    numero_serie character varying(100),
    fecha_vigencia_inicio timestamp without time zone,
    fecha_vigencia_fin timestamp without time zone
);


--
-- Name: empresas_empresa_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.empresas_empresa_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: empresas_empresa_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.empresas_empresa_id_seq OWNED BY public.empresas.empresa_id;


--
-- Name: empresas_sat; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.empresas_sat (
    empresa_id bigint NOT NULL,
    tipo_acceso_sat character varying(20) DEFAULT 'EFIRMA'::character varying NOT NULL,
    sat_usuario character varying(100),
    sat_password character varying(500),
    ruta_cer character varying(1000),
    ruta_key character varying(1000),
    password_key character varying(500),
    fecha_vencimiento date,
    usa_validacion_sat boolean DEFAULT true NOT NULL,
    usa_descarga_sat boolean DEFAULT true NOT NULL,
    usa_efos boolean DEFAULT true NOT NULL,
    usa_xml_azulejeras boolean DEFAULT false NOT NULL,
    fecha_creacion timestamp without time zone DEFAULT now() NOT NULL,
    fecha_actualizacion timestamp without time zone,
    descarga_automatica boolean DEFAULT false NOT NULL,
    descarga_emitidos boolean DEFAULT true NOT NULL,
    descarga_recibidos boolean DEFAULT true NOT NULL,
    descarga_metadatos boolean DEFAULT false NOT NULL,
    descarga_desde date,
    ciec_ultima_prueba timestamp without time zone,
    ciec_resultado text
);


--
-- Name: empresas_sinube; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.empresas_sinube (
    empresa_id bigint NOT NULL,
    empresa text DEFAULT ''::text NOT NULL,
    sucursal text DEFAULT 'Matriz'::text NOT NULL,
    usuario text DEFAULT ''::text NOT NULL,
    password text,
    ambiente text DEFAULT 'PRODUCCION'::text NOT NULL,
    modo_producto text DEFAULT 'NOIDENTIFICACION'::text NOT NULL,
    serie_folio_de_folio boolean DEFAULT false NOT NULL,
    agrupar_codigo_sat boolean DEFAULT false NOT NULL,
    usar_producto_clientes boolean DEFAULT false NOT NULL,
    validar_ieps boolean DEFAULT true NOT NULL,
    incluir_uuid boolean DEFAULT false NOT NULL,
    incluir_tipo boolean DEFAULT false NOT NULL,
    emisor_receptor text DEFAULT 'RFC'::text NOT NULL,
    criterio_nombre integer DEFAULT 1 NOT NULL,
    ultima_prueba timestamp without time zone,
    resultado_prueba text,
    fecha_actualizacion timestamp without time zone DEFAULT now() NOT NULL,
    foliador_llave text DEFAULT ''::text NOT NULL,
    foliador_nombre text DEFAULT ''::text NOT NULL,
    serie_alterna_pagos text DEFAULT ''::text NOT NULL,
    serie_alterna_azulejeras text DEFAULT ''::text NOT NULL,
    serie_alterna_proveedores text DEFAULT ''::text NOT NULL
);


--
-- Name: personas_fiscales; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.personas_fiscales (
    persona_id bigint NOT NULL,
    rfc character varying(13) NOT NULL,
    nombre character varying(300),
    tipo character varying(20),
    fecha_alta timestamp without time zone DEFAULT now()
);


--
-- Name: personas_fiscales_persona_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.personas_fiscales_persona_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: personas_fiscales_persona_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.personas_fiscales_persona_id_seq OWNED BY public.personas_fiscales.persona_id;


--
-- Name: proteccion_llaves; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.proteccion_llaves (
    nombre text NOT NULL,
    xml text NOT NULL,
    creada timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: respaldos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.respaldos (
    respaldo_id bigint NOT NULL,
    equipo text NOT NULL,
    tipo text NOT NULL,
    inicio timestamp without time zone DEFAULT now() NOT NULL,
    fin timestamp without time zone,
    estado text NOT NULL,
    archivo text DEFAULT ''::text NOT NULL,
    tamano bigint DEFAULT 0 NOT NULL,
    detalle text DEFAULT ''::text NOT NULL
);


--
-- Name: respaldos_respaldo_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.respaldos_respaldo_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: respaldos_respaldo_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.respaldos_respaldo_id_seq OWNED BY public.respaldos.respaldo_id;


--
-- Name: roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.roles (
    rol_id bigint NOT NULL,
    nombre text NOT NULL,
    nombre_normalizado text NOT NULL,
    descripcion text DEFAULT ''::text NOT NULL,
    concurrency_stamp text
);


--
-- Name: roles_rol_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.roles_rol_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: roles_rol_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.roles_rol_id_seq OWNED BY public.roles.rol_id;


--
-- Name: sat_cierres; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sat_cierres (
    empresa_id bigint NOT NULL,
    periodo integer NOT NULL,
    inicio timestamp without time zone NOT NULL,
    fin timestamp without time zone,
    estado text NOT NULL,
    metadatos_emitidos integer DEFAULT 0 NOT NULL,
    metadatos_recibidos integer DEFAULT 0 NOT NULL,
    xml_importados integer DEFAULT 0 NOT NULL,
    cancelados_sin_xml integer DEFAULT 0 NOT NULL,
    otros_sin_xml integer DEFAULT 0 NOT NULL,
    detalle text DEFAULT ''::text NOT NULL,
    automatico boolean DEFAULT false NOT NULL
);


--
-- Name: sat_descargas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sat_descargas (
    id bigint NOT NULL,
    empresa_id bigint,
    uuid character varying(50),
    fecha timestamp without time zone,
    rfc_emisor character varying(20),
    rfc_receptor character varying(20),
    tipo character varying(20),
    xml_path text,
    descargado boolean DEFAULT false
);


--
-- Name: sat_descargas_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.sat_descargas_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: sat_descargas_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.sat_descargas_id_seq OWNED BY public.sat_descargas.id;


--
-- Name: sat_metadatos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sat_metadatos (
    empresa_id bigint NOT NULL,
    recibido boolean NOT NULL,
    uuid text NOT NULL,
    rfc_emisor text DEFAULT ''::text NOT NULL,
    nombre_emisor text DEFAULT ''::text NOT NULL,
    rfc_receptor text DEFAULT ''::text NOT NULL,
    nombre_receptor text DEFAULT ''::text NOT NULL,
    fecha_emision timestamp without time zone NOT NULL,
    monto numeric(18,2) DEFAULT 0 NOT NULL,
    efecto text DEFAULT ''::text NOT NULL,
    estatus text DEFAULT ''::text NOT NULL,
    fecha_cancelacion timestamp without time zone
);


--
-- Name: sat_metadatos_periodos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sat_metadatos_periodos (
    empresa_id bigint NOT NULL,
    recibido boolean NOT NULL,
    periodo integer NOT NULL,
    solicitud_id text NOT NULL,
    registros integer NOT NULL,
    cubre_hasta timestamp without time zone NOT NULL,
    consultado timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: sat_paquetes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sat_paquetes (
    paquete_id bigint NOT NULL,
    solicitud_id bigint NOT NULL,
    identificador_sat character varying(200),
    fecha_descarga timestamp without time zone,
    archivo_zip bytea,
    procesado boolean DEFAULT false
);


--
-- Name: sat_paquetes_paquete_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.sat_paquetes_paquete_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: sat_paquetes_paquete_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.sat_paquetes_paquete_id_seq OWNED BY public.sat_paquetes.paquete_id;


--
-- Name: sat_solicitudes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sat_solicitudes (
    solicitud_id bigint NOT NULL,
    empresa_id bigint NOT NULL,
    certificado_id bigint NOT NULL,
    fecha_inicio date NOT NULL,
    fecha_fin date NOT NULL,
    tipo_descarga character varying(20),
    id_solicitud_sat character varying(100),
    estado character varying(50),
    fecha_solicitud timestamp without time zone DEFAULT now() NOT NULL,
    fecha_completado timestamp without time zone
);


--
-- Name: sat_solicitudes_solicitud_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.sat_solicitudes_solicitud_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: sat_solicitudes_solicitud_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.sat_solicitudes_solicitud_id_seq OWNED BY public.sat_solicitudes.solicitud_id;


--
-- Name: sinube_envios; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sinube_envios (
    empresa_id bigint NOT NULL,
    tipo character(1) NOT NULL,
    uuid text NOT NULL,
    archivo text DEFAULT ''::text NOT NULL,
    serie text DEFAULT ''::text NOT NULL,
    folio text DEFAULT ''::text NOT NULL,
    rfc text DEFAULT ''::text NOT NULL,
    nombre text DEFAULT ''::text NOT NULL,
    tipo_comprobante text DEFAULT ''::text NOT NULL,
    fecha_cfdi timestamp without time zone,
    estatus text NOT NULL,
    mensaje text DEFAULT ''::text NOT NULL,
    fecha_envio timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: storage_archivos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.storage_archivos (
    ruta text NOT NULL,
    empresa_id bigint NOT NULL,
    uuid text,
    modificado timestamp without time zone NOT NULL,
    tamano bigint NOT NULL,
    estatus text NOT NULL,
    detalle text,
    revisado timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: terceros; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.terceros (
    tercero_id bigint NOT NULL,
    rfc character varying(13) NOT NULL,
    razon_social character varying(300) NOT NULL,
    fecha_alta timestamp without time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: terceros_tercero_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.terceros_tercero_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: terceros_tercero_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.terceros_tercero_id_seq OWNED BY public.terceros.tercero_id;


--
-- Name: usuario_empresas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.usuario_empresas (
    usuario_id bigint NOT NULL,
    empresa_id bigint NOT NULL,
    fecha_asignacion timestamp without time zone DEFAULT now() NOT NULL
);


--
-- Name: usuario_roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.usuario_roles (
    usuario_id bigint NOT NULL,
    rol_id bigint NOT NULL
);


--
-- Name: usuario_tareas_denegadas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.usuario_tareas_denegadas (
    usuario_id bigint NOT NULL,
    tarea text NOT NULL
);


--
-- Name: usuarios; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.usuarios (
    usuario_id bigint NOT NULL,
    nombre_usuario text NOT NULL,
    nombre_usuario_normalizado text NOT NULL,
    nombre_completo text DEFAULT ''::text NOT NULL,
    email text DEFAULT ''::text NOT NULL,
    password_hash text,
    security_stamp text,
    concurrency_stamp text,
    lockout_habilitado boolean DEFAULT true NOT NULL,
    lockout_fin timestamp with time zone,
    accesos_fallidos integer DEFAULT 0 NOT NULL,
    activo boolean DEFAULT true NOT NULL,
    fecha_alta timestamp without time zone DEFAULT now() NOT NULL,
    ultimo_acceso timestamp without time zone,
    debe_cambiar_password boolean DEFAULT false NOT NULL
);


--
-- Name: usuarios_usuario_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.usuarios_usuario_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: usuarios_usuario_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.usuarios_usuario_id_seq OWNED BY public.usuarios.usuario_id;


--
-- Name: vw_cfdi_clientes; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_cfdi_clientes AS
 SELECT c.cfdi_id,
    c.uuid,
    c.fecha_emision,
    c.total,
    p.persona_id,
    p.rfc,
    p.nombre
   FROM (public.cfdi c
     JOIN public.personas_fiscales p ON ((c.receptor_id = p.persona_id)));


--
-- Name: vw_clientes_facturacion; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_clientes_facturacion AS
 SELECT p.persona_id,
    p.rfc,
    p.nombre,
    count(*) AS cantidad_cfdi,
    sum(c.total) AS total_facturado,
    min(c.fecha_emision) AS primera_factura,
    max(c.fecha_emision) AS ultima_factura
   FROM (public.cfdi c
     JOIN public.personas_fiscales p ON ((c.receptor_id = p.persona_id)))
  GROUP BY p.persona_id, p.rfc, p.nombre;


--
-- Name: archivos_cfdi archivo_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.archivos_cfdi ALTER COLUMN archivo_id SET DEFAULT nextval('public.archivos_cfdi_archivo_id_seq'::regclass);


--
-- Name: cfdi cfdi_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi ALTER COLUMN cfdi_id SET DEFAULT nextval('public.cfdi_cfdi_id_seq'::regclass);


--
-- Name: cfdi_conceptos concepto_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_conceptos ALTER COLUMN concepto_id SET DEFAULT nextval('public.cfdi_conceptos_concepto_id_seq'::regclass);


--
-- Name: cfdi_importaciones importacion_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_importaciones ALTER COLUMN importacion_id SET DEFAULT nextval('public.cfdi_importaciones_importacion_id_seq'::regclass);


--
-- Name: cfdi_impuestos impuesto_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_impuestos ALTER COLUMN impuesto_id SET DEFAULT nextval('public.cfdi_impuestos_impuesto_id_seq'::regclass);


--
-- Name: correos_enviados correo_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.correos_enviados ALTER COLUMN correo_id SET DEFAULT nextval('public.correos_enviados_correo_id_seq'::regclass);


--
-- Name: efos_sat id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.efos_sat ALTER COLUMN id SET DEFAULT nextval('public.efos_sat_id_seq'::regclass);


--
-- Name: efos_sat_cargas carga_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.efos_sat_cargas ALTER COLUMN carga_id SET DEFAULT nextval('public.efos_sat_cargas_carga_id_seq'::regclass);


--
-- Name: empresa_certificados certificado_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresa_certificados ALTER COLUMN certificado_id SET DEFAULT nextval('public.empresa_certificados_certificado_id_seq'::regclass);


--
-- Name: empresa_certificados_log log_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresa_certificados_log ALTER COLUMN log_id SET DEFAULT nextval('public.empresa_certificados_log_log_id_seq'::regclass);


--
-- Name: empresas empresa_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresas ALTER COLUMN empresa_id SET DEFAULT nextval('public.empresas_empresa_id_seq'::regclass);


--
-- Name: personas_fiscales persona_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.personas_fiscales ALTER COLUMN persona_id SET DEFAULT nextval('public.personas_fiscales_persona_id_seq'::regclass);


--
-- Name: respaldos respaldo_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.respaldos ALTER COLUMN respaldo_id SET DEFAULT nextval('public.respaldos_respaldo_id_seq'::regclass);


--
-- Name: roles rol_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles ALTER COLUMN rol_id SET DEFAULT nextval('public.roles_rol_id_seq'::regclass);


--
-- Name: sat_descargas id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_descargas ALTER COLUMN id SET DEFAULT nextval('public.sat_descargas_id_seq'::regclass);


--
-- Name: sat_paquetes paquete_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_paquetes ALTER COLUMN paquete_id SET DEFAULT nextval('public.sat_paquetes_paquete_id_seq'::regclass);


--
-- Name: sat_solicitudes solicitud_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_solicitudes ALTER COLUMN solicitud_id SET DEFAULT nextval('public.sat_solicitudes_solicitud_id_seq'::regclass);


--
-- Name: terceros tercero_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.terceros ALTER COLUMN tercero_id SET DEFAULT nextval('public.terceros_tercero_id_seq'::regclass);


--
-- Name: usuarios usuario_id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuarios ALTER COLUMN usuario_id SET DEFAULT nextval('public.usuarios_usuario_id_seq'::regclass);


--
-- Name: archivos_cfdi archivos_cfdi_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.archivos_cfdi
    ADD CONSTRAINT archivos_cfdi_pkey PRIMARY KEY (archivo_id);


--
-- Name: cfdi_conceptos cfdi_conceptos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_conceptos
    ADD CONSTRAINT cfdi_conceptos_pkey PRIMARY KEY (concepto_id);


--
-- Name: cfdi_importaciones cfdi_importaciones_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_importaciones
    ADD CONSTRAINT cfdi_importaciones_pkey PRIMARY KEY (importacion_id);


--
-- Name: cfdi_impuestos_globales cfdi_impuestos_globales_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_impuestos_globales
    ADD CONSTRAINT cfdi_impuestos_globales_pkey PRIMARY KEY (impuesto_global_id);


--
-- Name: cfdi_impuestos cfdi_impuestos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_impuestos
    ADD CONSTRAINT cfdi_impuestos_pkey PRIMARY KEY (impuesto_id);


--
-- Name: cfdi cfdi_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi
    ADD CONSTRAINT cfdi_pkey PRIMARY KEY (cfdi_id);


--
-- Name: cfdi_relacionados cfdi_relacionados_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_relacionados
    ADD CONSTRAINT cfdi_relacionados_pkey PRIMARY KEY (relacionado_id);


--
-- Name: cfdi_verificados cfdi_verificados_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_verificados
    ADD CONSTRAINT cfdi_verificados_pkey PRIMARY KEY (cfdi_id);


--
-- Name: configuracion_app configuracion_app_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.configuracion_app
    ADD CONSTRAINT configuracion_app_pkey PRIMARY KEY (clave);


--
-- Name: configuracion_correo configuracion_correo_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.configuracion_correo
    ADD CONSTRAINT configuracion_correo_pkey PRIMARY KEY (configuracion_id);


--
-- Name: correos_enviados correos_enviados_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.correos_enviados
    ADD CONSTRAINT correos_enviados_pkey PRIMARY KEY (correo_id);


--
-- Name: efos_sat_cargas efos_sat_cargas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.efos_sat_cargas
    ADD CONSTRAINT efos_sat_cargas_pkey PRIMARY KEY (carga_id);


--
-- Name: efos_sat efos_sat_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.efos_sat
    ADD CONSTRAINT efos_sat_pkey PRIMARY KEY (id);


--
-- Name: empresa_certificados_log empresa_certificados_log_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresa_certificados_log
    ADD CONSTRAINT empresa_certificados_log_pkey PRIMARY KEY (log_id);


--
-- Name: empresa_certificados empresa_certificados_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresa_certificados
    ADD CONSTRAINT empresa_certificados_pkey PRIMARY KEY (certificado_id);


--
-- Name: empresas empresas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresas
    ADD CONSTRAINT empresas_pkey PRIMARY KEY (empresa_id);


--
-- Name: empresas empresas_rfc_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresas
    ADD CONSTRAINT empresas_rfc_key UNIQUE (rfc);


--
-- Name: empresas_sat empresas_sat_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresas_sat
    ADD CONSTRAINT empresas_sat_pkey PRIMARY KEY (empresa_id);


--
-- Name: empresas_sinube empresas_sinube_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresas_sinube
    ADD CONSTRAINT empresas_sinube_pkey PRIMARY KEY (empresa_id);


--
-- Name: personas_fiscales personas_fiscales_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.personas_fiscales
    ADD CONSTRAINT personas_fiscales_pkey PRIMARY KEY (persona_id);


--
-- Name: personas_fiscales personas_fiscales_rfc_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.personas_fiscales
    ADD CONSTRAINT personas_fiscales_rfc_key UNIQUE (rfc);


--
-- Name: proteccion_llaves proteccion_llaves_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.proteccion_llaves
    ADD CONSTRAINT proteccion_llaves_pkey PRIMARY KEY (nombre);


--
-- Name: respaldos respaldos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.respaldos
    ADD CONSTRAINT respaldos_pkey PRIMARY KEY (respaldo_id);


--
-- Name: roles roles_nombre_normalizado_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_nombre_normalizado_key UNIQUE (nombre_normalizado);


--
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (rol_id);


--
-- Name: sat_cierres sat_cierres_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_cierres
    ADD CONSTRAINT sat_cierres_pkey PRIMARY KEY (empresa_id, periodo);


--
-- Name: sat_descargas sat_descargas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_descargas
    ADD CONSTRAINT sat_descargas_pkey PRIMARY KEY (id);


--
-- Name: sat_metadatos_periodos sat_metadatos_periodos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_metadatos_periodos
    ADD CONSTRAINT sat_metadatos_periodos_pkey PRIMARY KEY (empresa_id, recibido, periodo);


--
-- Name: sat_metadatos sat_metadatos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_metadatos
    ADD CONSTRAINT sat_metadatos_pkey PRIMARY KEY (empresa_id, recibido, uuid);


--
-- Name: sat_paquetes sat_paquetes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_paquetes
    ADD CONSTRAINT sat_paquetes_pkey PRIMARY KEY (paquete_id);


--
-- Name: sat_solicitudes sat_solicitudes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_solicitudes
    ADD CONSTRAINT sat_solicitudes_pkey PRIMARY KEY (solicitud_id);


--
-- Name: sinube_envios sinube_envios_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sinube_envios
    ADD CONSTRAINT sinube_envios_pkey PRIMARY KEY (empresa_id, tipo, uuid);


--
-- Name: storage_archivos storage_archivos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.storage_archivos
    ADD CONSTRAINT storage_archivos_pkey PRIMARY KEY (ruta);


--
-- Name: terceros terceros_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.terceros
    ADD CONSTRAINT terceros_pkey PRIMARY KEY (tercero_id);


--
-- Name: terceros terceros_rfc_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.terceros
    ADD CONSTRAINT terceros_rfc_key UNIQUE (rfc);


--
-- Name: usuario_empresas usuario_empresas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuario_empresas
    ADD CONSTRAINT usuario_empresas_pkey PRIMARY KEY (usuario_id, empresa_id);


--
-- Name: usuario_roles usuario_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuario_roles
    ADD CONSTRAINT usuario_roles_pkey PRIMARY KEY (usuario_id, rol_id);


--
-- Name: usuario_tareas_denegadas usuario_tareas_denegadas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuario_tareas_denegadas
    ADD CONSTRAINT usuario_tareas_denegadas_pkey PRIMARY KEY (usuario_id, tarea);


--
-- Name: usuarios usuarios_nombre_usuario_normalizado_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuarios
    ADD CONSTRAINT usuarios_nombre_usuario_normalizado_key UNIQUE (nombre_usuario_normalizado);


--
-- Name: usuarios usuarios_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuarios
    ADD CONSTRAINT usuarios_pkey PRIMARY KEY (usuario_id);


--
-- Name: efos_sat_rfc_upper; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX efos_sat_rfc_upper ON public.efos_sat USING btree (upper(TRIM(BOTH FROM rfc)));


--
-- Name: idx_cfdi_clasificacion_tipo_fecha; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_cfdi_clasificacion_tipo_fecha ON public.cfdi USING btree (clasificacion, tipo_cfdi, fecha_emision);


--
-- Name: idx_cfdi_estado_sat; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_cfdi_estado_sat ON public.cfdi USING btree (empresa_id, estado_sat);


--
-- Name: ix_cfdi_conceptos_cfdi; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_conceptos_cfdi ON public.cfdi_conceptos USING btree (cfdi_id);


--
-- Name: ix_cfdi_empresa_fecha_clasificacion_tipo; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_empresa_fecha_clasificacion_tipo ON public.cfdi USING btree (empresa_id, fecha_emision, clasificacion, tipo_cfdi);


--
-- Name: ix_cfdi_empresa_uuid_upper; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_empresa_uuid_upper ON public.cfdi USING btree (empresa_id, upper((uuid)::text));


--
-- Name: ix_cfdi_fecha_timbrado; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_fecha_timbrado ON public.cfdi USING btree (fecha_timbrado);


--
-- Name: ix_cfdi_forma_pago; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_forma_pago ON public.cfdi USING btree (forma_pago);


--
-- Name: ix_cfdi_impuestos_concepto; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_impuestos_concepto ON public.cfdi_impuestos USING btree (concepto_id);


--
-- Name: ix_cfdi_impuestos_globales_cfdi; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_impuestos_globales_cfdi ON public.cfdi_impuestos_globales USING btree (cfdi_id);


--
-- Name: ix_cfdi_metodo_pago; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_metodo_pago ON public.cfdi USING btree (metodo_pago);


--
-- Name: ix_cfdi_relacionados_cfdi; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_relacionados_cfdi ON public.cfdi_relacionados USING btree (cfdi_id);


--
-- Name: ix_cfdi_relacionados_uuid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_relacionados_uuid ON public.cfdi_relacionados USING btree (uuid_relacionado);


--
-- Name: ix_cfdi_relacionados_uuid_upper; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_relacionados_uuid_upper ON public.cfdi_relacionados USING btree (upper((uuid_relacionado)::text));


--
-- Name: ix_cfdi_uso_cfdi; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_uso_cfdi ON public.cfdi USING btree (uso_cfdi);


--
-- Name: ix_cfdi_uuid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cfdi_uuid ON public.cfdi USING btree (uuid);


--
-- Name: ix_correos_enviados_fecha; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_correos_enviados_fecha ON public.correos_enviados USING btree (fecha DESC);


--
-- Name: ix_efos_sat_rfc; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_efos_sat_rfc ON public.efos_sat USING btree (rfc);


--
-- Name: ix_sat_metadatos_fecha; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_sat_metadatos_fecha ON public.sat_metadatos USING btree (empresa_id, recibido, fecha_emision);


--
-- Name: ix_usuario_empresas_empresa; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_usuario_empresas_empresa ON public.usuario_empresas USING btree (empresa_id);


--
-- Name: storage_archivos_empresa; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX storage_archivos_empresa ON public.storage_archivos USING btree (empresa_id);


--
-- Name: storage_archivos_uuid; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX storage_archivos_uuid ON public.storage_archivos USING btree (empresa_id, uuid);


--
-- Name: ux_cfdi_empresa_uuid; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_cfdi_empresa_uuid ON public.cfdi USING btree (empresa_id, uuid);


--
-- Name: ux_cfdi_impuestos_globales_natural; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_cfdi_impuestos_globales_natural ON public.cfdi_impuestos_globales USING btree (cfdi_id, tipo, COALESCE(impuesto, ''::character varying), COALESCE(tipo_factor, ''::character varying), COALESCE(base, (0)::numeric), COALESCE(tasa, (0)::numeric), COALESCE(importe, (0)::numeric));


--
-- Name: ux_cfdi_relacionados_natural; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_cfdi_relacionados_natural ON public.cfdi_relacionados USING btree (cfdi_id, tipo_relacion, uuid_relacionado);


--
-- Name: cfdi_verificados cfdi_verificados_cfdi_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_verificados
    ADD CONSTRAINT cfdi_verificados_cfdi_id_fkey FOREIGN KEY (cfdi_id) REFERENCES public.cfdi(cfdi_id) ON DELETE CASCADE;


--
-- Name: archivos_cfdi fk_archivo_cfdi; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.archivos_cfdi
    ADD CONSTRAINT fk_archivo_cfdi FOREIGN KEY (cfdi_id) REFERENCES public.cfdi(cfdi_id) ON DELETE CASCADE;


--
-- Name: empresa_certificados fk_cert_empresa; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresa_certificados
    ADD CONSTRAINT fk_cert_empresa FOREIGN KEY (empresa_id) REFERENCES public.empresas(empresa_id);


--
-- Name: cfdi fk_cfdi_emisor; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi
    ADD CONSTRAINT fk_cfdi_emisor FOREIGN KEY (emisor_id) REFERENCES public.personas_fiscales(persona_id);


--
-- Name: cfdi fk_cfdi_empresa; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi
    ADD CONSTRAINT fk_cfdi_empresa FOREIGN KEY (empresa_id) REFERENCES public.empresas(empresa_id);


--
-- Name: cfdi_impuestos fk_cfdi_impuestos_concepto; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_impuestos
    ADD CONSTRAINT fk_cfdi_impuestos_concepto FOREIGN KEY (concepto_id) REFERENCES public.cfdi_conceptos(concepto_id);


--
-- Name: cfdi_impuestos_globales fk_cfdi_impuestos_globales_cfdi; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_impuestos_globales
    ADD CONSTRAINT fk_cfdi_impuestos_globales_cfdi FOREIGN KEY (cfdi_id) REFERENCES public.cfdi(cfdi_id) ON DELETE CASCADE;


--
-- Name: cfdi fk_cfdi_receptor; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi
    ADD CONSTRAINT fk_cfdi_receptor FOREIGN KEY (receptor_id) REFERENCES public.personas_fiscales(persona_id);


--
-- Name: cfdi_relacionados fk_cfdi_relacionados_cfdi; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_relacionados
    ADD CONSTRAINT fk_cfdi_relacionados_cfdi FOREIGN KEY (cfdi_id) REFERENCES public.cfdi(cfdi_id) ON DELETE CASCADE;


--
-- Name: cfdi_conceptos fk_concepto_cfdi; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cfdi_conceptos
    ADD CONSTRAINT fk_concepto_cfdi FOREIGN KEY (cfdi_id) REFERENCES public.cfdi(cfdi_id) ON DELETE CASCADE;


--
-- Name: empresas_sat fk_empresas_sat_empresa; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.empresas_sat
    ADD CONSTRAINT fk_empresas_sat_empresa FOREIGN KEY (empresa_id) REFERENCES public.empresas(empresa_id) ON DELETE CASCADE;


--
-- Name: sat_cierres sat_cierres_empresa_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_cierres
    ADD CONSTRAINT sat_cierres_empresa_id_fkey FOREIGN KEY (empresa_id) REFERENCES public.empresas(empresa_id) ON DELETE CASCADE;


--
-- Name: sat_metadatos sat_metadatos_empresa_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_metadatos
    ADD CONSTRAINT sat_metadatos_empresa_id_fkey FOREIGN KEY (empresa_id) REFERENCES public.empresas(empresa_id) ON DELETE CASCADE;


--
-- Name: sat_metadatos_periodos sat_metadatos_periodos_empresa_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sat_metadatos_periodos
    ADD CONSTRAINT sat_metadatos_periodos_empresa_id_fkey FOREIGN KEY (empresa_id) REFERENCES public.empresas(empresa_id) ON DELETE CASCADE;


--
-- Name: storage_archivos storage_archivos_empresa_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.storage_archivos
    ADD CONSTRAINT storage_archivos_empresa_id_fkey FOREIGN KEY (empresa_id) REFERENCES public.empresas(empresa_id) ON DELETE CASCADE;


--
-- Name: usuario_empresas usuario_empresas_empresa_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuario_empresas
    ADD CONSTRAINT usuario_empresas_empresa_id_fkey FOREIGN KEY (empresa_id) REFERENCES public.empresas(empresa_id) ON DELETE CASCADE;


--
-- Name: usuario_empresas usuario_empresas_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuario_empresas
    ADD CONSTRAINT usuario_empresas_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(usuario_id) ON DELETE CASCADE;


--
-- Name: usuario_roles usuario_roles_rol_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuario_roles
    ADD CONSTRAINT usuario_roles_rol_id_fkey FOREIGN KEY (rol_id) REFERENCES public.roles(rol_id) ON DELETE CASCADE;


--
-- Name: usuario_roles usuario_roles_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuario_roles
    ADD CONSTRAINT usuario_roles_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(usuario_id) ON DELETE CASCADE;


--
-- Name: usuario_tareas_denegadas usuario_tareas_denegadas_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuario_tareas_denegadas
    ADD CONSTRAINT usuario_tareas_denegadas_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(usuario_id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--


